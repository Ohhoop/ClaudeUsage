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

    private readonly AppSettings _settings;

    private UsageSnapshot? _snapshot;
    private FetchStatus _status = FetchStatus.Ok;
    private string[] _countdowns = Array.Empty<string>();
    private bool _allowVisible;
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
            _font?.Dispose();
        }

        base.Dispose(disposing);
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
