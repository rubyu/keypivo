using System.Drawing.Drawing2D;

namespace Keypivo;

// Keep native CheckBox keyboard and accessibility behavior while drawing a switch.
internal sealed class NotificationToggle : CheckBox
{
    public NotificationToggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        Size = new Size(360, 42);
        Margin = Padding.Empty;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var scale = DeviceDpi / 96f;
        var track = new RectangleF(Width - 38 * scale, (Height - 20 * scale) / 2, 36 * scale, 20 * scale);
        var foreground = Enabled ? ForeColor : SystemColors.GrayText;
        TextRenderer.DrawText(e.Graphics, Text, Font,
            new Rectangle(0, 0, Width - (int)(54 * scale), Height), foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = new GraphicsPath();
        shape.AddArc(track.X, track.Y, track.Height, track.Height, 90, 180);
        shape.AddArc(track.Right - track.Height, track.Y, track.Height, track.Height, 270, 180);
        shape.CloseFigure();
        var accent = Enabled ? AppTheme.Accent : SystemColors.GrayText;
        using var fill = new SolidBrush(Checked ? accent : BackColor);
        using var border = new Pen(Checked ? accent : foreground, scale);
        e.Graphics.FillPath(fill, shape);
        e.Graphics.DrawPath(border, shape);
        using var thumb = new SolidBrush(Checked ? AppTheme.OnAccent : foreground);
        e.Graphics.FillEllipse(thumb, Checked ? track.Right - 16 * scale : track.X + 4 * scale,
            track.Y + 4 * scale, 12 * scale, 12 * scale);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1));
    }
}
