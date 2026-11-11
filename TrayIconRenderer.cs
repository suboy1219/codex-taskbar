using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace CodexTaskbar;

internal static class TrayIconRenderer
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon Create(int? remaining, bool error)
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var color = error
            ? Color.FromArgb(255, 96, 88)
            : remaining switch
            {
                null => Color.FromArgb(160, 160, 166),
                >= 50 => Color.FromArgb(52, 199, 89),
                >= 20 => Color.FromArgb(255, 159, 10),
                _ => Color.FromArgb(255, 69, 58),
            };

        var text = error ? "!" : remaining?.ToString() ?? "…";
        using (var font = new Font("Segoe UI", text.Length >= 3 ? 17f : 20f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        })
        {
            g.DrawString(text, font, Brushes.White, new RectangleF(0, -2, 32, 30), format);
        }

        using (var trackBrush = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
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
}
