using System.Drawing.Drawing2D;

namespace Keypivo;

// Keep native CheckBox keyboard and accessibility behavior while drawing a switch.
internal sealed class NotificationToggle : CheckBox
{
    public NotificationToggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = AppTheme.Surface;
        UseVisualStyleBackColor = false;
        AutoSize = false;
        Size = new Size(440, 54);
        Margin = Padding.Empty;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var scale = DeviceDpi / 96f;
        var width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        var text = TextRenderer.MeasureText(Text, Font, new Size(Math.Max(1, width - (int)(64 * scale)), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        return new Size(width, Math.Max((int)(54 * scale), text.Height + (int)(24 * scale)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // ButtonBase is opaque: owner painting must cover the entire buffer on
        // every repaint, otherwise old text and uninitialized pixels remain.
        e.Graphics.SmoothingMode = SmoothingMode.None;
        using var background = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(background, ClientRectangle);
        var scale = DeviceDpi / 96f;
        var track = new RectangleF(Width - 46 * scale, (Height - 24 * scale) / 2, 44 * scale, 24 * scale);
        var foreground = Enabled ? ForeColor : SystemColors.GrayText;
        TextRenderer.DrawText(e.Graphics, Text, Font,
            new Rectangle(0, 0, Math.Max(1, Width - (int)(64 * scale)), Height), foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak |
            TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
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
        e.Graphics.FillEllipse(thumb, Checked ? track.Right - 20 * scale : track.X + 4 * scale,
            track.Y + 4 * scale, 16 * scale, 16 * scale);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1));
    }
}
