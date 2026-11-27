using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Keypivo;

internal enum PopupGlyph { Refresh, Settings, Power, Info }

// Native Button behavior (keyboard, accessibility, disabled actions) with
// DPI-independent artwork that does not depend on an installed icon font.
internal sealed class PopupButton : Button
{
    private readonly PopupGlyph? _glyph;
    private bool _hovered;
    private bool _pressed;
    private bool _selected;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; Invalidate(); }
    }

    public PopupButton(PopupGlyph? glyph = null)
    {
        _glyph = glyph;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        BackColor = glyph.HasValue ? AppTheme.Chrome : AppTheme.Surface;
        ForeColor = glyph.HasValue ? AppTheme.Muted : AppTheme.OnAccent;
        Size = glyph.HasValue ? new Size(40, 40) : new Size(PopupLayout.ContentWidth, 48);
        Margin = Padding.Empty;
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; base.OnMouseLeave(e); Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) _pressed = true; base.OnMouseDown(e); Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; base.OnMouseUp(e); Invalidate(); }
    protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) _pressed = false; base.OnMouseCaptureChanged(e); Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) _pressed = true; base.OnKeyDown(e); Invalidate(); }
    protected override void OnKeyUp(KeyEventArgs e) { _pressed = false; base.OnKeyUp(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { _pressed = false; base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { _pressed = false; base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.None;
        using var background = new SolidBrush(BackColor);
        graphics.FillRectangle(background, ClientRectangle);
        if (Width < 2 || Height < 2) return;

        var scale = DeviceDpi / 96f;
        var active = Enabled && (_hovered || _pressed);
        var foreground = Enabled ? ForeColor : SystemColors.GrayText;
        Color fill;
        if (_glyph.HasValue)
        {
            fill = active ? AppTheme.IconHover : AppTheme.IconSurface;
            if (active) foreground = AppTheme.Text;
            if (Selected)
            {
                foreground = AppTheme.Accent;
                fill = AppTheme.AccentSurface;
            }
            if (active && _glyph == PopupGlyph.Power)
            {
                foreground = AppTheme.Error;
                fill = AppTheme.ErrorSurface;
            }
        }
        else
        {
            fill = !Enabled ? AppTheme.Hover : active && !SystemInformation.HighContrast
                ? ControlPaint.Dark(AppTheme.Accent, _pressed ? .2f : .1f) : AppTheme.Accent;
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = RoundedRectangle(new RectangleF(.5f, .5f, Width - 1, Height - 1), 8 * scale);
        using var brush = new SolidBrush(fill);
        graphics.FillPath(brush, shape);
        if (_glyph is { } glyph)
        {
            var saved = graphics.Save();
            var iconScale = 20 * scale / 24;
            graphics.TranslateTransform((Width - 24 * iconScale) / 2, (Height - 24 * iconScale) / 2);
            graphics.ScaleTransform(iconScale, iconScale);
            using var pen = new Pen(foreground, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            DrawGlyph(graphics, pen, glyph);
            graphics.Restore(saved);
        }
        else
        {
            TextRenderer.DrawText(graphics, Text, Font, Rectangle.Inflate(ClientRectangle, -(int)(12 * scale), 0), foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
        }
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(ClientRectangle, -(int)(3 * scale), -(int)(3 * scale)), foreground, fill);
    }

    private static void DrawGlyph(Graphics graphics, Pen pen, PopupGlyph glyph)
    {
        switch (glyph)
        {
            case PopupGlyph.Refresh:
                graphics.DrawArc(pen, 4, 4, 16, 16, 220, 140);
                graphics.DrawLines(pen, [new PointF(20, 6), new PointF(20, 12), new PointF(14, 12)]);
                graphics.DrawArc(pen, 4, 4, 16, 16, 40, 140);
                graphics.DrawLines(pen, [new PointF(4, 18), new PointF(4, 12), new PointF(10, 12)]);
                break;
            case PopupGlyph.Settings:
                var points = Enumerable.Range(0, 32).Select(i =>
                {
                    var angle = i * Math.PI / 16;
                    var radius = i % 4 is 1 or 2 ? 9f : 7f;
                    return new PointF(12 + radius * (float)Math.Cos(angle), 12 + radius * (float)Math.Sin(angle));
                }).ToArray();
                graphics.DrawPolygon(pen, points);
                graphics.DrawEllipse(pen, 9, 9, 6, 6);
                break;
            case PopupGlyph.Power:
                graphics.DrawArc(pen, 4.5f, 4.5f, 15, 15, -50, 280);
                graphics.DrawLine(pen, 12, 2.5f, 12, 11);
                break;
            case PopupGlyph.Info:
                graphics.DrawEllipse(pen, 3, 3, 18, 18);
                graphics.DrawLine(pen, 12, 11, 12, 17);
                graphics.DrawLine(pen, 12, 7, 12, 7.1f);
                break;
        }
    }

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
