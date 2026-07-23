using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;

namespace ClaudeUsage;

public sealed class OverlayForm : Form
{
    private const int WidthLu = 280;
    private const int PadLu = 8;
    private const int LogoZoneLu = 26;
    private const int LogoRadiusLu = 9;
    private const int MainRowLu = 26;
    private const int GapLu = 4;
    private const int SubRowLu = 20;
    private const int RadiusLu = 10;
    private const int EdgeLu = 12;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private readonly AppSettings _settings;
    private readonly string _naText = Loc.T("na");
    private readonly System.Windows.Forms.Timer _presenceTimer = new();
    private readonly System.Windows.Forms.Timer _fetchTimer = new();
    private readonly System.Windows.Forms.Timer _tickTimer = new();
    private readonly ToolTip _toolTip = new();
    private readonly NotifyIcon _trayIcon = new();
    private readonly Dictionary<string, DateTimeOffset> _resetAt = new();
    private readonly HashSet<string> _notifiedResets = new();

    private UsageSnapshot? _snapshot;
    private FetchStatus _status = FetchStatus.Ok;
    private DateTimeOffset _lastSuccessUtc;
    private DateTimeOffset _nextFetchAllowedUtc = DateTimeOffset.MinValue;
    private string[] _countdowns = Array.Empty<string>();
    private bool _allowVisible;
    private bool _shown;
    private bool _fetching;
    private bool _sentToTray;
    private bool _claudeSeen;
    private DateTimeOffset? _claudeAbsentSince;
    private bool _dragging;
    private Point _dragOffset;
    private Font? _labelFont;
    private Font? _mainFont;
    private Font? _subFont;

    public OverlayForm()
    {
        _settings = SettingsStore.Load();

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(24, 24, 26);
        Opacity = _settings.OpacityPercent / 100.0;
        Text = "ClaudeUsage";

        UpdateSize();
        ApplyStoredPosition();
        BuildContextMenu();

        if (SessionHook.Exists() && !SessionHook.MatchesCurrentPath())
        {
            SessionHook.TryEnable();
        }

        _trayIcon.Icon = CreateTrayIcon();
        _trayIcon.Text = "ClaudeUsage";
        _trayIcon.ContextMenuStrip = ContextMenuStrip;
        _trayIcon.Visible = true;

        _presenceTimer.Interval = 200;
        _presenceTimer.Tick += OnPresenceTick;
        _presenceTimer.Start();

        _fetchTimer.Interval = 120_000;
        _fetchTimer.Tick += (_, _) => _ = FetchNowAsync();

        _tickTimer.Interval = 1_000;
        _tickTimer.Tick += OnCountdownTick;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
            return parameters;
        }
    }

    protected override void SetVisibleCore(bool value)
    {
        base.SetVisibleCore(_allowVisible && value);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        UpdateSize();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f;
        var pad = ScaleValue(PadLu, scale);

        using (var borderPen = new Pen(SeverityColors.Track))
        using (var borderPath = RoundedRect(new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1), ScaleValue(RadiusLu, scale)))
        {
            graphics.DrawPath(borderPen, borderPath);
        }

        DrawLogo(graphics, scale);

        var contentX = pad + ScaleValue(LogoZoneLu, scale);
        var contentWidth = ClientSize.Width - contentX - pad;
        var rows = _snapshot?.Rows;

        if (rows is null || rows.Count == 0)
        {
            var statusBounds = new Rectangle(contentX, 0, contentWidth, ClientSize.Height);
            TextRenderer.DrawText(graphics, StatusMessage(), GetLabelFont(), statusBounds, SeverityColors.MutedText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        else
        {
            var mainIndex = Math.Max(IndexOfKind(rows, "session"), 0);
            DrawMainRow(graphics, rows[mainIndex], mainIndex, contentX, pad, contentWidth, scale);

            var others = new List<int>();
            for (var i = 0; i < rows.Count && others.Count < 2; i++)
            {
                if (i != mainIndex)
                {
                    others.Add(i);
                }
            }

            var subTop = pad + ScaleValue(MainRowLu, scale) + ScaleValue(GapLu, scale);
            if (others.Count == 1)
            {
                DrawCompactCell(graphics, rows[others[0]], others[0], contentX, subTop, contentWidth, scale);
            }
            else if (others.Count == 2)
            {
                var cellGap = ScaleValue(10, scale);
                var cellWidth = (contentWidth - cellGap) / 2;
                DrawCompactCell(graphics, rows[others[0]], others[0], contentX, subTop, cellWidth, scale);
                DrawCompactCell(graphics, rows[others[1]], others[1], contentX + cellWidth + cellGap, subTop, cellWidth, scale);
            }
        }

        if (_status != FetchStatus.Ok && DateTimeOffset.UtcNow - _lastSuccessUtc > TimeSpan.FromMinutes(10))
        {
            using var dimBrush = new SolidBrush(Color.FromArgb(80, BackColor));
            graphics.FillRectangle(dimBrush, ClientRectangle);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _presenceTimer.Dispose();
            _fetchTimer.Dispose();
            _tickTimer.Dispose();
            _trayIcon.Visible = false;
            var icon = _trayIcon.Icon;
            _trayIcon.Dispose();
            icon?.Dispose();
            _toolTip.Dispose();
            _labelFont?.Dispose();
            _mainFont?.Dispose();
            _subFont?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnPresenceTick(object? sender, EventArgs e)
    {
        if (_presenceTimer.Interval != 5_000)
        {
            _presenceTimer.Interval = 5_000;
        }

        bool present;
        try
        {
            var processes = Process.GetProcessesByName("claude");
            present = processes.Length > 0;
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
        catch
        {
            return;
        }

        if (present)
        {
            _claudeSeen = true;
            _claudeAbsentSince = null;
        }
        else if (_claudeSeen)
        {
            _claudeAbsentSince ??= DateTimeOffset.UtcNow;
            if (DateTimeOffset.UtcNow - _claudeAbsentSince >= TimeSpan.FromSeconds(30))
            {
                Close();
                return;
            }
        }

        if (present && !_fetchTimer.Enabled)
        {
            _fetchTimer.Start();
            _ = FetchNowAsync();
        }
        else if (!present && _fetchTimer.Enabled)
        {
            _fetchTimer.Stop();
        }

        var shouldShow = present && !_sentToTray;
        if (shouldShow && !_shown)
        {
            ShowVisual();
        }
        else if (!shouldShow && _shown)
        {
            HideVisual();
        }

        if (_shown)
        {
            EnsureOnScreen();
        }

        ProcessResets();
    }

    private void UpdateResetTargets(UsageSnapshot snapshot)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var row in snapshot.Rows)
        {
            if (row.ResetsAt is not DateTimeOffset resetsAt)
            {
                continue;
            }

            if (!_resetAt.TryGetValue(row.Kind, out var current))
            {
                var effective = resetsAt;
                if (effective <= now)
                {
                    _notifiedResets.Add(ResetKey(row.Kind, resetsAt));
                    if (ResetWindow(row.Kind) is TimeSpan seed)
                    {
                        while (effective <= now)
                        {
                            effective += seed;
                        }
                    }
                }

                _resetAt[row.Kind] = effective;
            }
            else if (resetsAt > current)
            {
                _resetAt[row.Kind] = resetsAt;
            }
        }
    }

    private void ProcessResets()
    {
        var rows = _snapshot?.Rows;
        if (rows is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var rolled = false;
        foreach (var row in rows)
        {
            if (!_resetAt.TryGetValue(row.Kind, out var target) || now < target + TimeSpan.FromMinutes(1))
            {
                continue;
            }

            if (_notifiedResets.Add(ResetKey(row.Kind, target)) && _settings.NotifyOnReset)
            {
                _trayIcon.ShowBalloonTip(5000, "ClaudeUsage", string.Format(CultureInfo.CurrentCulture, Loc.T("notification.reset"), row.Label), ToolTipIcon.Info);
            }

            if (ResetWindow(row.Kind) is TimeSpan window)
            {
                var next = target;
                while (next <= now)
                {
                    next += window;
                }

                _resetAt[row.Kind] = next;
                rolled = true;
            }
        }

        if (rolled)
        {
            RefreshCountdowns();
            Invalidate();
        }
    }

    private DateTimeOffset? EffectiveReset(LimitRow row)
        => _resetAt.TryGetValue(row.Kind, out var eff) ? eff : row.ResetsAt;

    private static string ResetKey(string kind, DateTimeOffset resetsAt) => $"{kind}|{resetsAt.UtcTicks}";

    private static TimeSpan? ResetWindow(string kind) => kind switch
    {
        "session" => TimeSpan.FromHours(5),
        "weekly_all" => TimeSpan.FromDays(7),
        "weekly_scoped" => TimeSpan.FromDays(7),
        _ => null,
    };

    private void ShowVisual()
    {
        _shown = true;
        _allowVisible = true;
        Show();
        TopMost = false;
        TopMost = true;
        _tickTimer.Start();
        Invalidate();
    }

    private void HideVisual()
    {
        _shown = false;
        _tickTimer.Stop();
        Hide();
    }

    private async Task FetchNowAsync()
    {
        if (_fetching || DateTimeOffset.UtcNow < _nextFetchAllowedUtc)
        {
            return;
        }

        _fetching = true;
        try
        {
            var outcome = await UsageClient.FetchAsync();
            if (outcome.Status == FetchStatus.Ok && outcome.Snapshot is not null)
            {
                _snapshot = outcome.Snapshot;
                _lastSuccessUtc = DateTimeOffset.UtcNow;
                _status = FetchStatus.Ok;
                UpdateResetTargets(outcome.Snapshot);
                RefreshCountdowns();
                UpdateSize();
            }
            else if (outcome.Status == FetchStatus.RateLimited)
            {
                _status = FetchStatus.RateLimited;
                var delay = outcome.RetryAfter ?? TimeSpan.FromMinutes(5);
                if (delay < TimeSpan.FromSeconds(30))
                {
                    delay = TimeSpan.FromSeconds(30);
                }
                else if (delay > TimeSpan.FromHours(1))
                {
                    delay = TimeSpan.FromHours(1);
                }

                _nextFetchAllowedUtc = DateTimeOffset.UtcNow + delay;
            }
            else
            {
                _status = outcome.Status;
            }

            UpdateToolTip();
            Invalidate();
        }
        catch
        {
            _status = FetchStatus.Transient;
        }
        finally
        {
            _fetching = false;
        }
    }

    private void OnCountdownTick(object? sender, EventArgs e)
    {
        if (_snapshot is null)
        {
            return;
        }

        ProcessResets();
        var previous = _countdowns;
        RefreshCountdowns();
        if (!previous.SequenceEqual(_countdowns))
        {
            Invalidate();
        }
    }

    private void RefreshCountdowns()
    {
        var rows = _snapshot?.Rows;
        if (rows is null)
        {
            _countdowns = Array.Empty<string>();
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var values = new string[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            var resetsAt = EffectiveReset(rows[i]);
            values[i] = resetsAt is null ? _naText : CountdownFormatter.Format(resetsAt.Value - now);
        }

        _countdowns = values;
    }

    private void UpdateToolTip()
    {
        string text;
        if (_snapshot is null || _snapshot.Rows.Count == 0)
        {
            text = StatusMessage();
        }
        else
        {
            var culture = CultureInfo.CurrentCulture;
            var lines = new List<string>();
            foreach (var row in _snapshot.Rows)
            {
                var effective = EffectiveReset(row);
                var reset = effective is null
                    ? _naText
                    : effective.Value.ToLocalTime().ToString("ddd d MMM HH:mm", culture);
                lines.Add(string.Format(culture, Loc.T("tooltip.row"), row.Label, Math.Round(row.Percent), reset));
            }

            lines.Add(string.Format(culture, Loc.T("tooltip.updatedAt"), _lastSuccessUtc.ToLocalTime().ToString("HH:mm:ss", culture)));
            switch (_status)
            {
                case FetchStatus.NoToken:
                    lines.Add(Loc.T("status.noToken"));
                    break;
                case FetchStatus.AuthExpired:
                    lines.Add(Loc.T("tooltip.authExpired"));
                    break;
                case FetchStatus.RateLimited:
                    lines.Add(string.Format(culture, Loc.T("tooltip.rateLimited"), _nextFetchAllowedUtc.ToLocalTime().ToString("HH:mm", culture)));
                    break;
                case FetchStatus.Transient:
                    lines.Add(Loc.T("tooltip.stale"));
                    break;
            }

            text = string.Join(Environment.NewLine, lines);
        }

        _toolTip.SetToolTip(this, text);
    }

    private void EnsureOnScreen()
    {
        if (!IsOnAnyScreen())
        {
            MoveToDefaultPosition();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _dragOffset = e.Location;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            var screenPoint = PointToScreen(e.Location);
            Location = new Point(screenPoint.X - _dragOffset.X, screenPoint.Y - _dragOffset.Y);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragging)
        {
            _dragging = false;
            _settings.X = Location.X;
            _settings.Y = Location.Y;
            SettingsStore.Save(_settings);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _settings.X = Location.X;
        _settings.Y = Location.Y;
        SettingsStore.Save(_settings);
        _presenceTimer.Stop();
        _fetchTimer.Stop();
        _tickTimer.Stop();
        base.OnFormClosing(e);
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem(Loc.T("menu.refresh"), null, (_, _) => _ = FetchNowAsync()));

        var opacityMenu = new ToolStripMenuItem(Loc.T("menu.opacity"));
        foreach (var level in SettingsStore.OpacityLevels)
        {
            var item = new ToolStripMenuItem($"{level} %") { Checked = _settings.OpacityPercent == level, Tag = level };
            item.Click += OnOpacityItemClick;
            opacityMenu.DropDownItems.Add(item);
        }

        menu.Items.Add(opacityMenu);

        var notifyItem = new ToolStripMenuItem(Loc.T("menu.notifyOnReset")) { Checked = _settings.NotifyOnReset, CheckOnClick = true };
        notifyItem.CheckedChanged += (_, _) =>
        {
            _settings.NotifyOnReset = notifyItem.Checked;
            SettingsStore.Save(_settings);
            if (notifyItem.Checked)
            {
                _trayIcon.ShowBalloonTip(4000, "ClaudeUsage", Loc.T("notification.enabled"), ToolTipIcon.Info);
            }
        };
        menu.Items.Add(notifyItem);

        var hookItem = new ToolStripMenuItem(Loc.T("menu.launchWithClaude")) { Checked = SessionHook.Exists() };
        hookItem.Click += (_, _) =>
        {
            if (SessionHook.Exists())
            {
                SessionHook.TryDisable();
            }
            else
            {
                SessionHook.TryEnable();
            }

            hookItem.Checked = SessionHook.Exists();
        };
        menu.Items.Add(hookItem);

        var trayItem = new ToolStripMenuItem(Loc.T("menu.sendToTray"));
        trayItem.Click += (_, _) =>
        {
            _sentToTray = !_sentToTray;
            if (_sentToTray)
            {
                HideVisual();
            }
            else
            {
                ShowVisual();
            }
        };
        menu.Items.Add(trayItem);

        menu.Opening += (_, _) =>
        {
            hookItem.Checked = SessionHook.Exists();
            trayItem.Text = _sentToTray ? Loc.T("menu.show") : Loc.T("menu.sendToTray");
        };

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(Loc.T("menu.quit"), null, (_, _) => Close()));
        ContextMenuStrip = menu;
    }

    private static Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(24, 24, 26));
            using var trackBrush = new SolidBrush(SeverityColors.Track);
            using var fillBrush = new SolidBrush(SeverityColors.Neutral);
            graphics.FillRectangle(trackBrush, 2, 2, 12, 3);
            graphics.FillRectangle(fillBrush, 2, 2, 9, 3);
            graphics.FillRectangle(trackBrush, 2, 7, 12, 3);
            graphics.FillRectangle(fillBrush, 2, 7, 4, 3);
            graphics.FillRectangle(trackBrush, 2, 12, 12, 3);
            graphics.FillRectangle(fillBrush, 2, 12, 6, 3);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private void OnOpacityItemClick(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem item || item.Tag is not int level || item.OwnerItem is not ToolStripMenuItem parent)
        {
            return;
        }

        _settings.OpacityPercent = level;
        Opacity = level / 100.0;
        foreach (ToolStripItem sibling in parent.DropDownItems)
        {
            if (sibling is ToolStripMenuItem menuItem)
            {
                menuItem.Checked = ReferenceEquals(menuItem, item);
            }
        }

        SettingsStore.Save(_settings);
    }

    private void DrawMainRow(Graphics graphics, LimitRow row, int index, int x, int top, int width, float scale)
    {
        var color = SeverityColors.ForLimit(row.Severity, row.Percent);
        var labelFont = GetLabelFont();
        var mainFont = GetMainFont();
        var countdown = index < _countdowns.Length ? _countdowns[index] : string.Empty;
        var percentText = $"{Math.Round(row.Percent)} %";

        TextRenderer.DrawText(graphics, row.Label, labelFont, new Point(x, top + ScaleValue(3, scale)), SeverityColors.MutedText, TextFormatFlags.NoPadding);
        var labelWidth = TextRenderer.MeasureText(graphics, row.Label, labelFont, Size.Empty, TextFormatFlags.NoPadding).Width;
        TextRenderer.DrawText(graphics, percentText, mainFont, new Point(x + labelWidth + ScaleValue(7, scale), top), color, TextFormatFlags.NoPadding);

        var countdownSize = TextRenderer.MeasureText(graphics, countdown, labelFont, Size.Empty, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(graphics, countdown, labelFont, new Point(x + width - countdownSize.Width, top + ScaleValue(3, scale)), SeverityColors.Text, TextFormatFlags.NoPadding);

        var barTop = top + ScaleValue(18, scale);
        var barHeight = Math.Max(ScaleValue(6, scale), 3);
        DrawBar(graphics, x, barTop, width, barHeight, row.Percent, color);
    }

    private void DrawCompactCell(Graphics graphics, LimitRow row, int index, int x, int top, int width, float scale)
    {
        var color = SeverityColors.ForLimit(row.Severity, row.Percent);
        var subFont = GetSubFont();
        var percentText = $"{Math.Round(row.Percent)} %";
        var countdown = index < _countdowns.Length ? _countdowns[index] : string.Empty;
        var percentSize = TextRenderer.MeasureText(graphics, percentText, subFont, Size.Empty, TextFormatFlags.NoPadding);
        var countdownSize = TextRenderer.MeasureText(graphics, countdown, subFont, Size.Empty, TextFormatFlags.NoPadding);

        TextRenderer.DrawText(graphics, row.Label, subFont, new Point(x, top), SeverityColors.MutedText, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(graphics, countdown, subFont, new Point(x + width - countdownSize.Width, top), SeverityColors.MutedText, TextFormatFlags.NoPadding);

        var margin = ScaleValue(6, scale);
        var barHeight = Math.Max(ScaleValue(4, scale), 2);
        var barY = top + ScaleValue(14, scale);
        var barWidth = width - percentSize.Width - margin;
        if (barWidth > 0)
        {
            DrawBar(graphics, x, barY, barWidth, barHeight, row.Percent, color);
        }

        TextRenderer.DrawText(graphics, percentText, subFont, new Point(x + width - percentSize.Width, top + ScaleValue(10, scale)), color, TextFormatFlags.NoPadding);
    }

    private static void DrawBar(Graphics graphics, int x, int y, int width, int height, double percent, Color color)
    {
        var radius = Math.Max(height / 2, 1);
        using (var trackBrush = new SolidBrush(SeverityColors.Track))
        using (var trackPath = RoundedRect(new Rectangle(x, y, width, height), radius))
        {
            graphics.FillPath(trackBrush, trackPath);
        }

        var fillWidth = (int)Math.Round(width * Math.Clamp(percent, 0, 100) / 100.0);
        if (fillWidth > 0 && fillWidth < height)
        {
            fillWidth = height;
        }

        if (fillWidth > 0)
        {
            using var fillBrush = new SolidBrush(color);
            using var fillPath = RoundedRect(new Rectangle(x, y, fillWidth, height), radius);
            graphics.FillPath(fillBrush, fillPath);
        }
    }

    private static readonly float[] RayJitter = { 0f, 4f, -3f, 5f, -4f, 2f, -5f, 3f, -2f, 4f };
    private static readonly float[] RayLength = { 1f, 0.82f, 0.95f, 0.78f, 1f, 0.85f, 0.92f, 0.8f, 0.97f, 0.84f };
    private static readonly Image? Spark = LoadSpark();

    private static Image? LoadSpark()
    {
        try
        {
            using var stream = typeof(OverlayForm).Assembly.GetManifestResourceStream("ClaudeUsage.Assets.spark.png");
            return stream is null ? null : Image.FromStream(stream);
        }
        catch
        {
            return null;
        }
    }

    private void DrawLogo(Graphics graphics, float scale)
    {
        var pad = ScaleValue(PadLu, scale);
        var zone = ScaleValue(LogoZoneLu, scale);

        if (Spark is not null)
        {
            var size = ScaleValue(20, scale);
            var x = pad + (zone - size) / 2 - ScaleValue(2, scale);
            var y = (ClientSize.Height - size) / 2;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(Spark, new Rectangle(x, y, size, size));
            return;
        }

        var centerX = pad + zone / 2f - 2f * scale;
        var centerY = ClientSize.Height / 2f;
        var outer = LogoRadiusLu * scale;
        using var brush = new SolidBrush(Color.FromArgb(217, 119, 87));

        for (var i = 0; i < RayJitter.Length; i++)
        {
            var angle = 2 * Math.PI * i / RayJitter.Length + RayJitter[i] * Math.PI / 180;
            var tip = outer * RayLength[i];
            var baseRadius = outer * 0.16f;
            var halfWidth = outer * 0.11f;
            var cos = (float)Math.Cos(angle);
            var sin = (float)Math.Sin(angle);
            var points = new[]
            {
                new PointF(centerX + cos * baseRadius - sin * halfWidth, centerY + sin * baseRadius + cos * halfWidth),
                new PointF(centerX + cos * tip, centerY + sin * tip),
                new PointF(centerX + cos * baseRadius + sin * halfWidth, centerY + sin * baseRadius - cos * halfWidth),
            };
            graphics.FillPolygon(brush, points);
        }
    }

    private static int IndexOfKind(IReadOnlyList<LimitRow> rows, string kind)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Kind == kind)
            {
                return i;
            }
        }

        return -1;
    }

    private string StatusMessage() => _status switch
    {
        FetchStatus.NoToken => Loc.T("status.noToken"),
        FetchStatus.AuthExpired => Loc.T("status.authExpired"),
        FetchStatus.RateLimited => Loc.T("status.rateLimited"),
        FetchStatus.Transient => Loc.T("status.connecting"),
        _ => Loc.T("status.loading"),
    };

    private void UpdateSize()
    {
        var scale = DeviceDpi / 96f;
        var rows = _snapshot?.Rows;
        var hasSubRow = rows is null || rows.Count > 1;
        var height = ScaleValue(PadLu, scale) * 2 + ScaleValue(MainRowLu, scale);
        if (hasSubRow)
        {
            height += ScaleValue(GapLu, scale) + ScaleValue(SubRowLu, scale);
        }

        ClientSize = new Size(ScaleValue(WidthLu, scale), height);
        ApplyRoundedRegion();
    }

    private void ApplyRoundedRegion()
    {
        using var path = RoundedRect(new Rectangle(0, 0, Width, Height), ScaleValue(RadiusLu, DeviceDpi / 96f));
        var previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.X;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void ApplyStoredPosition()
    {
        if (_settings.X is int x && _settings.Y is int y)
        {
            Location = new Point(x, y);
            if (IsOnAnyScreen())
            {
                return;
            }
        }

        MoveToDefaultPosition();
    }

    private void MoveToDefaultPosition()
    {
        var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        var area = screen.WorkingArea;
        var edge = ScaleValue(EdgeLu, DeviceDpi / 96f);
        Location = new Point(area.Right - Width - edge, area.Top + edge);
    }

    private bool IsOnAnyScreen()
    {
        var bounds = Bounds;
        foreach (var screen in Screen.AllScreens)
        {
            if (screen.WorkingArea.IntersectsWith(bounds))
            {
                return true;
            }
        }

        return false;
    }

    private Font GetLabelFont() => _labelFont ??= new Font("Segoe UI", 8.25f, FontStyle.Regular, GraphicsUnit.Point);

    private Font GetMainFont() => _mainFont ??= new Font("Segoe UI", 9.75f, FontStyle.Bold, GraphicsUnit.Point);

    private Font GetSubFont() => _subFont ??= new Font("Segoe UI", 7.5f, FontStyle.Regular, GraphicsUnit.Point);

    private static int ScaleValue(int logical, float scale) => (int)Math.Round(logical * scale);
}
