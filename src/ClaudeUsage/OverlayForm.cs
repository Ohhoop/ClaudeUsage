using System.Diagnostics;
using System.Globalization;

namespace ClaudeUsage;

public sealed class OverlayForm : Form
{
    private const int WidthLu = 210;
    private const int PadLu = 8;
    private const int RowLu = 22;
    private const int BarLu = 4;
    private const int BarOffsetLu = 14;
    private const int EdgeLu = 12;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-CA");

    private readonly AppSettings _settings;
    private readonly System.Windows.Forms.Timer _presenceTimer = new();
    private readonly System.Windows.Forms.Timer _fetchTimer = new();
    private readonly System.Windows.Forms.Timer _tickTimer = new();
    private readonly ToolTip _toolTip = new();

    private UsageSnapshot? _snapshot;
    private FetchStatus _status = FetchStatus.Ok;
    private DateTimeOffset _lastSuccessUtc;
    private string[] _countdowns = Array.Empty<string>();
    private bool _allowVisible;
    private bool _shown;
    private bool _fetching;
    private bool _dragging;
    private Point _dragOffset;
    private Font? _font;

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

        _presenceTimer.Interval = 200;
        _presenceTimer.Tick += OnPresenceTick;
        _presenceTimer.Start();

        _fetchTimer.Interval = 60_000;
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
        var scale = DeviceDpi / 96f;
        var font = GetFont();
        var pad = ScaleValue(PadLu, scale);
        var width = ClientSize.Width;

        using (var borderPen = new Pen(SeverityColors.Track))
        {
            graphics.DrawRectangle(borderPen, 0, 0, width - 1, ClientSize.Height - 1);
        }

        var rows = _snapshot?.Rows;
        if (rows is null || rows.Count == 0)
        {
            TextRenderer.DrawText(graphics, StatusMessage(), font, ClientRectangle, SeverityColors.MutedText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            DrawRow(graphics, font, rows[i], i, pad, width, scale);
        }

        if (_status != FetchStatus.Ok)
        {
            using var dimBrush = new SolidBrush(Color.FromArgb(140, BackColor));
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
            _toolTip.Dispose();
            _font?.Dispose();
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

        if (present && !_shown)
        {
            ShowOverlay();
        }
        else if (!present && _shown)
        {
            HideOverlay();
        }

        if (_shown)
        {
            EnsureOnScreen();
        }
    }

    private void ShowOverlay()
    {
        _shown = true;
        _allowVisible = true;
        Show();
        TopMost = false;
        TopMost = true;
        _fetchTimer.Start();
        _tickTimer.Start();
        _ = FetchNowAsync();
    }

    private void HideOverlay()
    {
        _shown = false;
        _fetchTimer.Stop();
        _tickTimer.Stop();
        Hide();
    }

    private async Task FetchNowAsync()
    {
        if (_fetching)
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
                RefreshCountdowns();
                UpdateSize();
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

        var previous = _countdowns;
        RefreshCountdowns();
        if (previous.SequenceEqual(_countdowns))
        {
            return;
        }

        if (previous.Length == _countdowns.Length)
        {
            for (var i = 0; i < previous.Length; i++)
            {
                if (previous[i] != _countdowns[i] && _countdowns[i] == "0 min")
                {
                    _ = FetchNowAsync();
                    break;
                }
            }
        }

        Invalidate();
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
            var resetsAt = rows[i].ResetsAt;
            values[i] = resetsAt is null ? "n/d" : CountdownFormatter.Format(resetsAt.Value - now);
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
            var lines = new List<string>();
            foreach (var row in _snapshot.Rows)
            {
                var reset = row.ResetsAt is null
                    ? "n/d"
                    : row.ResetsAt.Value.ToLocalTime().ToString("ddd d MMM HH:mm", French);
                lines.Add($"{row.Label} : {Math.Round(row.Percent)} %, reset {reset}");
            }

            lines.Add($"Mis à jour à {_lastSuccessUtc.ToLocalTime().ToString("HH:mm:ss", French)}");
            switch (_status)
            {
                case FetchStatus.NoToken:
                    lines.Add("Jeton introuvable");
                    break;
                case FetchStatus.AuthExpired:
                    lines.Add("Jeton expiré, ouvrez Claude");
                    break;
                case FetchStatus.Transient:
                    lines.Add("Données périmées, nouvelle tentative en cours");
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
        menu.Items.Add(new ToolStripMenuItem("Actualiser", null, (_, _) => _ = FetchNowAsync()));

        var opacityMenu = new ToolStripMenuItem("Opacité");
        foreach (var level in SettingsStore.OpacityLevels)
        {
            var item = new ToolStripMenuItem($"{level} %") { Checked = _settings.OpacityPercent == level, Tag = level };
            item.Click += OnOpacityItemClick;
            opacityMenu.DropDownItems.Add(item);
        }

        menu.Items.Add(opacityMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quitter", null, (_, _) => Close()));
        ContextMenuStrip = menu;
    }

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

    private void DrawRow(Graphics graphics, Font font, LimitRow row, int index, int pad, int width, float scale)
    {
        var top = pad + ScaleValue(RowLu, scale) * index;
        var color = SeverityColors.ForLimit(row.Severity, row.Percent);
        var countdown = index < _countdowns.Length ? _countdowns[index] : string.Empty;
        var percentText = $"{Math.Round(row.Percent)} %";
        var separator = " · ";
        var right = width - pad;

        TextRenderer.DrawText(graphics, row.Label, font, new Point(pad, top), SeverityColors.Text, TextFormatFlags.NoPadding);

        var countdownSize = TextRenderer.MeasureText(graphics, countdown, font, Size.Empty, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(graphics, countdown, font, new Point(right - countdownSize.Width, top), SeverityColors.MutedText, TextFormatFlags.NoPadding);

        var separatorSize = TextRenderer.MeasureText(graphics, separator, font, Size.Empty, TextFormatFlags.NoPadding);
        var percentSize = TextRenderer.MeasureText(graphics, percentText, font, Size.Empty, TextFormatFlags.NoPadding);
        var separatorX = right - countdownSize.Width - separatorSize.Width;
        TextRenderer.DrawText(graphics, separator, font, new Point(separatorX, top), SeverityColors.MutedText, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(graphics, percentText, font, new Point(separatorX - percentSize.Width, top), color, TextFormatFlags.NoPadding);

        var barTop = top + ScaleValue(BarOffsetLu, scale);
        var barHeight = Math.Max(ScaleValue(BarLu, scale), 2);
        var barWidth = width - pad * 2;
        using (var trackBrush = new SolidBrush(SeverityColors.Track))
        {
            graphics.FillRectangle(trackBrush, pad, barTop, barWidth, barHeight);
        }

        var fillWidth = (int)Math.Round(barWidth * Math.Clamp(row.Percent, 0, 100) / 100.0);
        if (fillWidth > 0)
        {
            using var fillBrush = new SolidBrush(color);
            graphics.FillRectangle(fillBrush, pad, barTop, fillWidth, barHeight);
        }
    }

    private string StatusMessage() => _status switch
    {
        FetchStatus.NoToken => "Jeton introuvable",
        FetchStatus.AuthExpired => "Jeton expiré",
        FetchStatus.Transient => "Connexion...",
        _ => "Chargement...",
    };

    private void UpdateSize()
    {
        var scale = DeviceDpi / 96f;
        var rowCount = Math.Max(_snapshot?.Rows.Count ?? 3, 1);
        ClientSize = new Size(ScaleValue(WidthLu, scale), ScaleValue(PadLu, scale) * 2 + ScaleValue(RowLu, scale) * rowCount);
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

    private Font GetFont()
    {
        _font ??= new Font("Segoe UI", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
        return _font;
    }

    private static int ScaleValue(int logical, float scale) => (int)Math.Round(logical * scale);
}
