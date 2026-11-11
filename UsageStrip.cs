using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodexTaskbar;

internal sealed class UsageStrip : Form
{
    private const int LogicalWidth = 160;
    private const int LogicalHeight = 24;

    private static readonly Color BackgroundTop = Color.FromArgb(42, 42, 47);
    private static readonly Color BackgroundBottom = Color.FromArgb(23, 23, 26);
    private static readonly Color BorderColor = Color.FromArgb(62, 62, 68);
    private static readonly Color BorderHoverColor = Color.FromArgb(92, 92, 100);
    private static readonly Color LabelColor = Color.FromArgb(150, 150, 159);
    private static readonly Color ValueColor = Color.FromArgb(244, 244, 247);
    private static readonly Color MutedColor = Color.FromArgb(140, 140, 148);
    private static readonly Color DangerColor = Color.FromArgb(255, 96, 88);
    private static readonly Color SuccessColor = Color.FromArgb(52, 199, 89);
    private static readonly Color WarningColor = Color.FromArgb(255, 159, 10);

    private readonly Font _labelFont = new("Microsoft YaHei UI", 8.2f, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _valueFont = new("Segoe UI Semibold", 9.4f, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _messageFont = new("Microsoft YaHei UI", 8.6f, FontStyle.Regular, GraphicsUnit.Point);

    private readonly AppSettings _settings;
    private UsageSnapshot? _snapshot;
    private bool _error;
    private bool _hover;
    private bool _dragging;
    private bool _moved;
    private bool _suppressClick;
    private Point _dragOrigin;
    private Point _dragStartLocation;

    public event EventHandler? Clicked;

    public UsageStrip(AppSettings settings)
    {
        _settings = settings;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = BackgroundTop;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        Cursor = Cursors.Hand;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
            parameters.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            parameters.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyScale();
        RestorePosition();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyScale();
        RestorePosition();
    }

    private float DpiScale => DeviceDpi / 96f;

    private Size VisibleSize
    {
        get
        {
            var s = DpiScale;
            return new Size(
                (int)Math.Round(LogicalWidth * s),
                (int)Math.Round(LogicalHeight * s));
        }
    }

    private void ApplyScale()
    {
        var size = VisibleSize;
        ClientSize = size;
        using var path = RoundedRect(new RectangleF(0, 0, size.Width, size.Height), size.Height / 2f);
        Region = new Region(path);
    }

    public void PositionAtBottom()
    {
        var s = DpiScale;
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromPoint(Cursor.Position).WorkingArea;
        var size = VisibleSize;
        var desired = new Point(
            area.Right - size.Width - (int)Math.Round(12 * s),
            area.Bottom - size.Height - (int)Math.Round(8 * s));

        var clientOrigin = PointToScreen(Point.Empty);
        Location += new Size(desired.X - clientOrigin.X, desired.Y - clientOrigin.Y);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RestorePosition();
    }

    public void ResetPosition()
    {
        _settings.StripLeft = null;
        _settings.StripTop = null;
        _settings.Save();
        PositionAtBottom();
    }

    private void RestorePosition()
    {
        var s = DpiScale;
        if (_settings.StripLeft is int left && _settings.StripTop is int top)
        {
            var size = VisibleSize;
            var bounds = new Rectangle(left, top, size.Width, size.Height);
            var area = Screen.FromRectangle(bounds).WorkingArea;
            var visible = Rectangle.Intersect(bounds, area);
            if (visible.Width >= 40 * s && visible.Height >= 16 * s)
            {
                Location = new Point(left, top);
                return;
            }
        }

        PositionAtBottom();
    }

    public void ShowSnapshot(UsageSnapshot snapshot)
    {
        _snapshot = snapshot;
        _error = false;
        Invalidate();
    }

    public void ShowError()
    {
        _error = true;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragging = true;
        _moved = false;
        _suppressClick = false;
        _dragOrigin = Cursor.Position;
        _dragStartLocation = Location;
        Capture = true;
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
        {
            return;
        }

        var current = Cursor.Position;
        var deltaX = current.X - _dragOrigin.X;
        var deltaY = current.Y - _dragOrigin.Y;
        if (!_moved && (Math.Abs(deltaX) > 6 || Math.Abs(deltaY) > 6))
        {
            _moved = true;
        }

        if (!_moved)
        {
            return;
        }

        Location = ClampToWorkingArea(new Point(_dragStartLocation.X + deltaX, _dragStartLocation.Y + deltaY));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !_dragging)
        {
            return;
        }

        _dragging = false;
        Capture = false;
        Cursor = Cursors.Hand;

        if (_moved)
        {
            SnapAndSave();
            _suppressClick = true;
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (_suppressClick)
        {
            _suppressClick = false;
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            Clicked?.Invoke(this, EventArgs.Empty);
        }
    }

    private Point ClampToWorkingArea(Point target)
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var size = VisibleSize;
        var x = Math.Clamp(target.X, area.Left, Math.Max(area.Left, area.Right - size.Width));
        var y = Math.Clamp(target.Y, area.Top, Math.Max(area.Top, area.Bottom - size.Height));
        return new Point(x, y);
    }

    private void SnapAndSave()
    {
        var s = DpiScale;
        var area = Screen.FromRectangle(Bounds).WorkingArea;
        var snap = (int)Math.Round(12 * s);
        var margin = (int)Math.Round(8 * s);
        var size = VisibleSize;
        var x = Location.X;
        var y = Location.Y;

        if (x - area.Left <= snap)
        {
            x = area.Left + margin;
        }

        if (area.Right - (x + size.Width) <= snap)
        {
            x = area.Right - size.Width - margin;
        }

        if (y - area.Top <= snap)
        {
            y = area.Top + margin;
        }

        if (area.Bottom - (y + size.Height) <= snap)
        {
            y = area.Bottom - size.Height - margin;
        }

        Location = new Point(x, y);
        _settings.StripLeft = x;
        _settings.StripTop = y;
        _settings.Save();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var s = DpiScale;
        float width = LogicalWidth * s;
        float height = LogicalHeight * s;

        using (var background = new LinearGradientBrush(
                   new RectangleF(0, 0, width, height),
                   _hover ? Lighten(BackgroundTop, 10) : BackgroundTop,
                   _hover ? Lighten(BackgroundBottom, 8) : BackgroundBottom,
                   90f))
        {
            g.FillRectangle(background, 0, 0, width, height);
        }

        using (var borderPath = RoundedRect(new RectangleF(0.5f, 0.5f, width - 1, height - 1), height / 2f))
        using (var border = new Pen(_hover ? BorderHoverColor : BorderColor, 1f))
        {
            g.DrawPath(border, borderPath);
        }

        using (var highlight = new Pen(Color.FromArgb(16, 255, 255, 255), 1f))
        {
            g.DrawLine(highlight, 12 * s, 1.5f * s, width - 12 * s, 1.5f * s);
        }

        if (_error || _snapshot == null)
        {
            DrawDot(g, 12 * s, height / 2f, _error ? DangerColor : WarningColor, s);
            TextRenderer.DrawText(
                g,
                _error ? "额度获取失败" : "正在获取额度…",
                _messageFont,
                new Rectangle((int)(21 * s), 0, (int)(width - 30 * s), (int)height),
                _error ? DangerColor : MutedColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            return;
        }

        var primaryText = _snapshot.HasPrimary ? $"{_snapshot.PrimaryRemaining}%" : "—";
        var secondaryText = _snapshot.HasSecondary ? $"{_snapshot.SecondaryRemaining}%" : "—";
        var primaryColor = ColorFor(_snapshot.HasPrimary, _snapshot.PrimaryRemaining);
        var secondaryColor = ColorFor(_snapshot.HasSecondary, _snapshot.SecondaryRemaining);

        var primaryLabel = ShortLabel(_snapshot.PrimaryLabel);
        var secondaryLabel = ShortLabel(_snapshot.SecondaryLabel);
        var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        var measureFlags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        var primaryLabelSize = TextRenderer.MeasureText(g, primaryLabel, _labelFont, new Size(int.MaxValue, int.MaxValue), measureFlags);
        var primaryValueSize = TextRenderer.MeasureText(g, primaryText, _valueFont, new Size(int.MaxValue, int.MaxValue), measureFlags);
        var secondaryLabelSize = TextRenderer.MeasureText(g, secondaryLabel, _labelFont, new Size(int.MaxValue, int.MaxValue), measureFlags);
        var secondaryValueSize = TextRenderer.MeasureText(g, secondaryText, _valueFont, new Size(int.MaxValue, int.MaxValue), measureFlags);

        var dotSize = 5 * s;
        var afterDot = 4 * s;
        var afterLabel = 3 * s;
        var dividerGap = 8 * s;
        var segmentOne = dotSize + afterDot + primaryLabelSize.Width + afterLabel + primaryValueSize.Width;
        var segmentTwo = dotSize + afterDot + secondaryLabelSize.Width + afterLabel + secondaryValueSize.Width;
        var contentWidth = segmentOne + dividerGap * 2 + 1 + segmentTwo;
        var x = (width - contentWidth) / 2f;
        var centerY = height / 2f;

        DrawDot(g, x + dotSize / 2f, centerY, primaryColor, s);
        x += dotSize + afterDot;
        TextRenderer.DrawText(g, primaryLabel, _labelFont, new Rectangle((int)x, 0, primaryLabelSize.Width + 2, (int)height), LabelColor, flags);
        x += primaryLabelSize.Width + afterLabel;
        TextRenderer.DrawText(g, primaryText, _valueFont, new Rectangle((int)x, 0, primaryValueSize.Width + 2, (int)height), ValueColor, flags);
        x += primaryValueSize.Width + dividerGap;

        using (var divider = new Pen(Color.FromArgb(24, 255, 255, 255), 1f))
        {
            g.DrawLine(divider, x, 6 * s, x, height - 6 * s);
        }

        x += 1 + dividerGap;
        DrawDot(g, x + dotSize / 2f, centerY, secondaryColor, s);
        x += dotSize + afterDot;
        TextRenderer.DrawText(g, secondaryLabel, _labelFont, new Rectangle((int)x, 0, secondaryLabelSize.Width + 2, (int)height), LabelColor, flags);
        x += secondaryLabelSize.Width + afterLabel;
        TextRenderer.DrawText(g, secondaryText, _valueFont, new Rectangle((int)x, 0, secondaryValueSize.Width + 2, (int)height), ValueColor, flags);
    }

    private static string ShortLabel(string label)
    {
        if (label.Contains('周'))
        {
            return "周";
        }

        var digits = new string(label.TakeWhile(char.IsDigit).ToArray());
        return digits.Length > 0 ? digits + "h" : "额度";
    }

    private static Color ColorFor(bool hasValue, int remaining) =>
        !hasValue
            ? MutedColor
            : remaining >= 50
                ? SuccessColor
                : remaining >= 20
                    ? WarningColor
                    : DangerColor;

    private static Color Lighten(Color color, int amount) =>
        Color.FromArgb(
            color.A,
            Math.Min(255, color.R + amount),
            Math.Min(255, color.G + amount),
            Math.Min(255, color.B + amount));

    private static void DrawDot(Graphics g, float centerX, float centerY, Color color, float scale)
    {
        var size = 5 * scale;
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, centerX - size / 2f, centerY - size / 2f, size, size);
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
