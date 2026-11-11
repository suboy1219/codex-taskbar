using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodexTaskbar;

internal sealed class UsagePanel : Form
{
    private const int LogicalWidth = 320;
    private const int LogicalHeight = 226;
    private const int HeaderHeight = 44;
    private const int CardTop = 52;
    private const int CardHeight = 64;
    private const int CardGap = 8;
    private const int FooterTop = 196;

    private static readonly Color BackgroundTop = Color.FromArgb(35, 35, 39);
    private static readonly Color BackgroundBottom = Color.FromArgb(21, 21, 24);
    private static readonly Color BorderColor = Color.FromArgb(60, 60, 66);
    private static readonly Color LabelColor = Color.FromArgb(158, 158, 167);
    private static readonly Color ValueColor = Color.FromArgb(245, 245, 248);
    private static readonly Color MutedColor = Color.FromArgb(134, 134, 142);
    private static readonly Color AccentColor = Color.FromArgb(140, 172, 255);
    private static readonly Color DangerColor = Color.FromArgb(255, 96, 88);
    private static readonly Color SuccessColor = Color.FromArgb(52, 199, 89);
    private static readonly Color WarningColor = Color.FromArgb(255, 159, 10);

    private readonly Font _titleFont = new("Microsoft YaHei UI", 10.2f, FontStyle.Bold, GraphicsUnit.Point);
    private readonly Font _labelFont = new("Microsoft YaHei UI", 8.8f, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _percentFont = new("Microsoft YaHei UI", 10f, FontStyle.Bold, GraphicsUnit.Point);
    private readonly Font _resetFont = new("Microsoft YaHei UI", 7.6f, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _footerFont = new("Microsoft YaHei UI", 7.8f, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _chipFont = new("Segoe UI", 7f, FontStyle.Bold, GraphicsUnit.Point);
    private readonly Font _buttonFont = new("Microsoft YaHei UI", 8f, FontStyle.Regular, GraphicsUnit.Point);
    private readonly System.Windows.Forms.Timer _outsideTimer;

    private UsageSnapshot? _snapshot;
    private string? _error;
    private bool _loading;
    private bool _hoverRefresh;
    private bool _mouseWasDown;
    private Rectangle _refreshRect;
    private int _reservedBottom;
    private DateTime _shownAt = DateTime.MinValue;

    public event EventHandler? RefreshRequested;

    public DateTime LastHiddenAt { get; private set; } = DateTime.MinValue;

    public UsagePanel()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = BackgroundTop;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        Cursor = Cursors.Default;

        _outsideTimer = new System.Windows.Forms.Timer { Interval = 90 };
        _outsideTimer.Tick += (_, _) => CheckOutsideClick();
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
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyScale();
    }

    private float DpiScale => DeviceDpi / 96f;

    private void ApplyScale()
    {
        var s = DpiScale;
        ClientSize = new Size(
            (int)Math.Round(LogicalWidth * s),
            (int)Math.Round(LogicalHeight * s));
        using var path = RoundedRect(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), 12 * s);
        Region = new Region(path);
    }

    public void ShowSnapshot(UsageSnapshot snapshot)
    {
        _snapshot = snapshot;
        _error = null;
        Invalidate();
    }

    public void ShowError(string message)
    {
        _error = message;
        Invalidate();
    }

    public void SetLoading(bool loading)
    {
        _loading = loading;
        Invalidate();
    }

    public void ShowNearTray(int reservedBottom = 0)
    {
        _reservedBottom = reservedBottom;
        if (Handle == IntPtr.Zero)
        {
            _ = Handle;
        }
        if (!Visible)
        {
            Show();
        }

        PositionAtTray();
        BringToFront();
    }

    public void ShowNearStrip(Rectangle? stripBounds)
    {
        if (stripBounds is not Rectangle anchor || anchor.IsEmpty)
        {
            ShowNearTray();
            return;
        }

        if (Handle == IntPtr.Zero)
        {
            _ = Handle;
        }

        if (!Visible)
        {
            Show();
        }

        PositionNearAnchor(anchor);
        BringToFront();
    }

    private void PositionNearAnchor(Rectangle anchor)
    {
        var s = DpiScale;
        var area = Screen.FromRectangle(anchor).WorkingArea;
        var margin = (int)Math.Round(8 * s);
        var maxX = Math.Max(area.Left + margin, area.Right - ClientSize.Width - margin);
        var x = Math.Clamp(anchor.Right - ClientSize.Width, area.Left + margin, maxX);
        var y = anchor.Top - ClientSize.Height - margin;
        if (y < area.Top + margin)
        {
            y = anchor.Bottom + margin;
        }

        if (y + ClientSize.Height > area.Bottom - margin)
        {
            y = Math.Max(area.Top + margin, area.Bottom - ClientSize.Height - margin);
        }

        var origin = PointToScreen(Point.Empty);
        Location += new Size(x - origin.X, y - origin.Y);
    }

    private void PositionAtTray()
    {
        var s = DpiScale;
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromPoint(Cursor.Position).WorkingArea;
        var margin = (int)Math.Round(12 * s);
        var desired = new Point(
            area.Right - ClientSize.Width - margin,
            area.Bottom - ClientSize.Height - margin - _reservedBottom);

        var origin = PointToScreen(Point.Empty);
        Location += new Size(desired.X - origin.X, desired.Y - origin.Y);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            _mouseWasDown = true;
            _shownAt = DateTime.UtcNow;
            _outsideTimer.Start();
        }
        else
        {
            _outsideTimer.Stop();
            LastHiddenAt = DateTime.UtcNow;
        }
    }

    private void CheckOutsideClick()
    {
        var isDown = (MouseButtons & MouseButtons.Left) == MouseButtons.Left;
        var settleDelay = (DateTime.UtcNow - _shownAt).TotalMilliseconds > 250;
        if (isDown && !_mouseWasDown && settleDelay && !Bounds.Contains(Cursor.Position))
        {
            Hide();
        }

        _mouseWasDown = isDown;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hover = _refreshRect.Contains(e.Location);
        if (hover != _hoverRefresh)
        {
            _hoverRefresh = hover;
            Cursor = hover ? Cursors.Hand : Cursors.Default;
            Invalidate(_refreshRect);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverRefresh)
        {
            _hoverRefresh = false;
            Cursor = Cursors.Default;
            Invalidate(_refreshRect);
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && _refreshRect.Contains(e.Location))
        {
            RefreshRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnMouseClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var s = DpiScale;
        float width = ClientSize.Width;
        float height = ClientSize.Height;
        float padX = 16 * s;

        using (var background = new LinearGradientBrush(
                   new RectangleF(0, 0, width, height),
                   BackgroundTop,
                   BackgroundBottom,
                   90f))
        {
            g.FillRectangle(background, 0, 0, width, height);
        }

        using (var borderPath = RoundedRect(new RectangleF(0.5f, 0.5f, width - 1, height - 1), 12 * s))
        using (var borderPen = new Pen(BorderColor, 1f))
        {
            g.DrawPath(borderPen, borderPath);
        }

        using (var highlight = new Pen(Color.FromArgb(18, 255, 255, 255), 1f))
        {
            g.DrawLine(highlight, padX + 6 * s, 1.5f * s, width - padX - 6 * s, 1.5f * s);
        }

        DrawHeader(g, s, width, padX);
        DrawCards(g, s, width, padX);
        DrawFooter(g, s, width, padX);
    }

    private void DrawHeader(Graphics g, float s, float width, float padX)
    {
        var statusColor = CurrentStatusColor();
        var centerY = 22 * s;

        using (var glow = new SolidBrush(Color.FromArgb(34, statusColor)))
        {
            g.FillEllipse(glow, 19 * s - 5 * s, centerY - 5 * s, 10 * s, 10 * s);
        }

        using (var dot = new SolidBrush(statusColor))
        {
            g.FillEllipse(dot, 19 * s - 3 * s, centerY - 3 * s, 6 * s, 6 * s);
        }

        TextRenderer.DrawText(
            g,
            "Codex 用量",
            _titleFont,
            new Rectangle((int)(29 * s), (int)(8 * s), (int)(width * 0.5f), (int)(28 * s)),
            Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        var chipText = string.IsNullOrWhiteSpace(_snapshot?.PlanType)
            ? "CODEX"
            : _snapshot!.PlanType.Trim().ToUpperInvariant();
        var textSize = TextRenderer.MeasureText(
            g,
            chipText,
            _chipFont,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        var chipWidth = Math.Max(44 * s, textSize.Width + 14 * s);
        var chipRect = new RectangleF(width - padX - chipWidth, 12.5f * s, chipWidth, 19 * s);

        using (var chipPath = RoundedRect(chipRect, 9.5f * s))
        using (var chipFill = new SolidBrush(Color.FromArgb(28, 122, 167, 255)))
        using (var chipStroke = new Pen(Color.FromArgb(48, 140, 172, 255), 1f))
        {
            g.FillPath(chipFill, chipPath);
            g.DrawPath(chipStroke, chipPath);
        }

        TextRenderer.DrawText(
            g,
            chipText,
            _chipFont,
            Rectangle.Round(chipRect),
            AccentColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        using var headerLine = new Pen(Color.FromArgb(20, 255, 255, 255), 1f);
        g.DrawLine(headerLine, padX, HeaderHeight * s, width - padX, HeaderHeight * s);
    }

    private void DrawCards(Graphics g, float s, float width, float padX)
    {
        var primaryLabel = _snapshot?.PrimaryLabel ?? "5 小时限额";
        var secondaryLabel = _snapshot?.SecondaryLabel ?? "周限额";
        var primaryPercent = _snapshot is { HasPrimary: true } p ? p.PrimaryRemaining : (int?)null;
        var secondaryPercent = _snapshot is { HasSecondary: true } w ? w.SecondaryRemaining : (int?)null;
        var cardWidth = width - padX * 2;

        DrawUsageCard(
            g,
            s,
            new RectangleF(padX, CardTop * s, cardWidth, CardHeight * s),
            primaryLabel,
            primaryPercent,
            FormatReset(_snapshot?.PrimaryResetAt));

        DrawUsageCard(
            g,
            s,
            new RectangleF(padX, (CardTop + CardHeight + CardGap) * s, cardWidth, CardHeight * s),
            secondaryLabel,
            secondaryPercent,
            FormatReset(_snapshot?.SecondaryResetAt));
    }

    private void DrawUsageCard(Graphics g, float s, RectangleF rect, string label, int? percent, string resetText)
    {
        using (var cardPath = RoundedRect(rect, 8 * s))
        using (var cardFill = new SolidBrush(Color.FromArgb(16, 255, 255, 255)))
        using (var cardStroke = new Pen(Color.FromArgb(16, 255, 255, 255), 1f))
        {
            g.FillPath(cardFill, cardPath);
            g.DrawPath(cardStroke, cardPath);
        }

        var innerPad = 12 * s;
        var contentLeft = (int)(rect.X + innerPad);
        var contentRight = (int)(rect.Right - innerPad);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        TextRenderer.DrawText(
            g,
            label,
            _labelFont,
            new Rectangle(contentLeft, (int)(rect.Y + 6 * s), (int)(rect.Width * 0.5f), (int)(18 * s)),
            LabelColor,
            flags | TextFormatFlags.Left);

        var value = percent is int pv ? $"剩余 {pv}%" : "—";
        TextRenderer.DrawText(
            g,
            value,
            _percentFont,
            new Rectangle(contentLeft, (int)(rect.Y + 4 * s), contentRight - contentLeft, (int)(22 * s)),
            percent is null ? MutedColor : ColorFor(percent),
            flags | TextFormatFlags.Right | TextFormatFlags.EndEllipsis);

        var barX = contentLeft;
        var barY = rect.Y + 32 * s;
        var barWidth = contentRight - contentLeft;
        var barHeight = 4 * s;
        using (var trackPath = RoundedRect(new RectangleF(barX, barY, barWidth, barHeight), barHeight / 2f))
        using (var trackBrush = new SolidBrush(Color.FromArgb(30, 255, 255, 255)))
        {
            g.FillPath(trackBrush, trackPath);
        }

        var fillWidth = barWidth * (percent ?? 0) / 100f;
        if (fillWidth > 0.5f)
        {
            using var fillPath = RoundedRect(new RectangleF(barX, barY, fillWidth, barHeight), barHeight / 2f);
            using var fillBrush = new SolidBrush(percent is null ? MutedColor : ColorFor(percent));
            g.FillPath(fillBrush, fillPath);
        }

        TextRenderer.DrawText(
            g,
            resetText == "—" ? "重置时间未知" : $"{resetText} 重置",
            _resetFont,
            new Rectangle(contentLeft, (int)(rect.Y + 40 * s), contentRight - contentLeft, (int)(16 * s)),
            MutedColor,
            flags | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    private void DrawFooter(Graphics g, float s, float width, float padX)
    {
        var footerTop = FooterTop * s;
        using (var separator = new Pen(Color.FromArgb(26, 255, 255, 255), 1f))
        {
            g.DrawLine(separator, padX, footerTop, width - padX, footerTop);
        }

        var footerText = _loading
            ? "正在同步…"
            : _error != null
                ? _error
                : _snapshot != null
                    ? $"{_snapshot.CreditsText ?? "—"} · {_snapshot.FetchedAt:HH:mm} 更新"
                    : "尚未获取数据";
        var footerColor = _error != null && !_loading ? DangerColor : MutedColor;

        TextRenderer.DrawText(
            g,
            footerText,
            _footerFont,
            new Rectangle(
                (int)padX,
                (int)(footerTop + 6 * s),
                (int)(width - padX * 2 - 60 * s),
                (int)(22 * s)),
            footerColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        _refreshRect = Rectangle.Round(new RectangleF(width - padX - 48 * s, footerTop + 7 * s, 48 * s, 22 * s));
        using (var buttonPath = RoundedRect(new RectangleF(_refreshRect.X, _refreshRect.Y, _refreshRect.Width, _refreshRect.Height), 11 * s))
        using (var buttonFill = new SolidBrush(_hoverRefresh ? Color.FromArgb(34, 255, 255, 255) : Color.FromArgb(18, 255, 255, 255)))
        using (var buttonStroke = new Pen(Color.FromArgb(_hoverRefresh ? 54 : 30, 255, 255, 255), 1f))
        {
            g.FillPath(buttonFill, buttonPath);
            g.DrawPath(buttonStroke, buttonPath);
        }

        TextRenderer.DrawText(
            g,
            _loading ? "同步中" : "刷新",
            _buttonFont,
            _refreshRect,
            _hoverRefresh ? Color.White : AccentColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    private Color CurrentStatusColor()
    {
        if (_loading)
        {
            return WarningColor;
        }

        if (_error != null)
        {
            return DangerColor;
        }

        if (_snapshot == null)
        {
            return MutedColor;
        }

        if (_snapshot.HasPrimary && _snapshot.PrimaryRemaining < 20)
        {
            return WarningColor;
        }

        return SuccessColor;
    }

    private static Color ColorFor(int? percent) =>
        percent switch
        {
            null => MutedColor,
            >= 50 => SuccessColor,
            >= 20 => WarningColor,
            _ => DangerColor,
        };

    private static string FormatReset(DateTimeOffset? value)
    {
        if (value is not DateTimeOffset reset)
        {
            return "—";
        }

        var local = reset.ToLocalTime();
        var today = DateTimeOffset.Now.Date;
        if (local.Date == today)
        {
            return local.ToString("HH:mm");
        }

        if (local.Date == today.AddDays(1))
        {
            return $"明天 {local:HH:mm}";
        }

        return local.ToString("M/d HH:mm");
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
