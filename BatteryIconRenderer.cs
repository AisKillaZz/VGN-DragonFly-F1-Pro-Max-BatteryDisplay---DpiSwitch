using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace VgnTrayBattery;

internal static class BatteryIconRenderer
{
    public static bool IsLow(int? percent) => percent is < 20;

    public static string ToTooltipText(int? percent)
    {
        if (percent is not int value) return "电量：--";
        return $"电量：{Math.Clamp(value, 0, 100)}%";
    }

    public static Icon CreateIcon(int? percent)
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            // A larger, square-cornered battery body.  The fill rectangle
            // uses the same inset on all four sides so it stays centered.
            // Keep the horizontal length, but reduce the height slightly for
            // a flatter battery silhouette.
            var body = new Rectangle(1, 6, 27, 20);
            // Start at the body's right edge so the full 5-pixel cap remains
            // visible inside the 32-pixel icon canvas.
            var cap = new Rectangle(27, 11, 5, 10);
            // Use fractional geometry so all four sides have exactly the
            // requested 2.5-pixel gap from the outer frame.
            const float inset = 2.5f;
            var inner = new RectangleF(body.X + inset, body.Y + inset,
                body.Width - inset * 2, body.Height - inset * 2);

            using var whitePen = new Pen(Color.White, 2.5f);
            using var whiteBrush = new SolidBrush(Color.White);
            graphics.DrawRectangle(whitePen, body);
            graphics.FillRectangle(whiteBrush, cap);
            if (percent is int value && value > 0)
            {
                var fillWidth = Math.Max(1f, inner.Width * Math.Clamp(value, 0, 100) / 100f);
                graphics.FillRectangle(whiteBrush, new RectangleF(inner.X, inner.Y, fillWidth, inner.Height));
            }
        }

        var handle = bitmap.GetHicon();
        var icon = (Icon)Icon.FromHandle(handle).Clone();
        DestroyIcon(handle);
        return icon;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
