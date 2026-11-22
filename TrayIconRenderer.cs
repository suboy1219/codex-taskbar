using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CodexTaskbar;

internal static class TrayIconRenderer
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon Create(int? remaining, bool error)
    {
        return Create(remaining, error, UsesLightTheme());
    }

    internal static Icon Create(int? remaining, bool error, bool lightTheme)
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var foreground = SystemInformation.HighContrast
            ? SystemColors.WindowText
            : lightTheme ? Color.FromArgb(32, 32, 32) : Color.White;
        var color = error
            ? (lightTheme ? Color.FromArgb(196, 43, 35) : Color.FromArgb(255, 96, 88))
            : remaining switch
            {
                null => Color.FromArgb(160, 160, 166),
                >= 50 => lightTheme ? Color.FromArgb(22, 128, 61) : Color.FromArgb(52, 199, 89),
                >= 20 => lightTheme ? Color.FromArgb(168, 88, 0) : Color.FromArgb(255, 159, 10),
                _ => lightTheme ? Color.FromArgb(196, 43, 35) : Color.FromArgb(255, 69, 58),
            };
        if (SystemInformation.HighContrast)
        {
            color = foreground;
        }

        var text = error ? "!" : remaining?.ToString() ?? "…";
        using (var font = new Font("Segoe UI", text.Length >= 3 ? 19f : 24f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var textBrush = new SolidBrush(foreground))
        using (var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
        })
        {
            g.DrawString(text, font, textBrush, new RectangleF(0, -2, 32, 28), format);
        }

        using (var trackBrush = new SolidBrush(Color.FromArgb(70, foreground)))
        {
            g.FillRectangle(trackBrush, 5f, 26f, 22f, 3f);
        }

        var percentValue = error ? 100 : Math.Clamp(remaining ?? 0, 0, 100);
        if (percentValue > 0)
        {
            using var barBrush = new SolidBrush(color);
            g.FillRectangle(barBrush, 5f, 26f, 22f * percentValue / 100f, 3f);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static bool UsesLightTheme()
    {
        try
        {
            // The notification area follows the Windows theme, not the app theme.
            return Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "SystemUsesLightTheme", null) is int value
                ? value != 0
                : SystemColors.Control.GetBrightness() > 0.5f;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return SystemColors.Control.GetBrightness() > 0.5f;
        }
    }
}
