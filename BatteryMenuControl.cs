using System.Drawing.Drawing2D;

namespace VgnTrayBattery;

internal sealed class BatteryMenuControl : Control
{
    private int? _batteryPercent;

    public int? BatteryPercent
    {
        get => _batteryPercent;
        set { _batteryPercent = value; Invalidate(); }
    }

    public BatteryMenuControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.White;
        ForeColor = Color.FromArgb(32, 32, 32);
        Size = new Size(238, 53);
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        using var labelFont = new Font("Segoe UI", 9f, FontStyle.Regular);
        using var labelBrush = new SolidBrush(ForeColor);
        var label = _batteryPercent is int percent
            ? $"电量  {Math.Clamp(percent, 0, 100)}%"
            : "电量  --";
        g.DrawString(label, labelFont, labelBrush, 12, 7);

        var track = new Rectangle(12, 29, 205, 10);
        using var trackBrush = new SolidBrush(Color.FromArgb(225, 225, 225));
        g.FillRectangle(trackBrush, track);
        if (_batteryPercent is int value && value > 0)
        {
            var fill = Color.FromArgb(32, 32, 32);
            using var fillBrush = new SolidBrush(fill);
            var width = Math.Max(2, track.Width * Math.Clamp(value, 0, 100) / 100);
            g.FillRectangle(fillBrush, new Rectangle(track.X, track.Y, width, track.Height));
        }
        using var borderPen = new Pen(Color.FromArgb(155, 155, 155));
        g.DrawRectangle(borderPen, track);
    }
}
