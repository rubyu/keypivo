using Keypivo.Core;

namespace Keypivo;

internal sealed class DeviceDetailsPanel : Panel
{
    private readonly (Label Caption, Label Value)[] _rows;
    private readonly Panel _separator = new() { Name = "VendorSeparator", BackColor = AppTheme.Border };
    private DeviceDetails? _details;
    private bool _expanded;
    private bool _arranging;
    private int _maximumHeight = int.MaxValue;
    private Font? _captionFont;
    private Font? _valueFont;
    private Font? _monoFont;

    internal bool Expanded => _expanded;

    internal DeviceDetailsPanel()
    {
        Name = "DeviceDetails";
        Width = PopupLayout.Width;
        Margin = new Padding(0, 24, 0, 0);
        BackColor = AppTheme.Chrome;
        AutoScroll = true;
        DoubleBuffered = true;
        _rows = new[] { "Product", "Vendor", "Description", "Connection", "Sharing", "Bus ID", "Persisted GUID" }
            .Select(caption => (
                new Label { Name = caption.Replace(" ", "") + "Caption", Text = caption + ":", ForeColor = AppTheme.Muted, UseMnemonic = false },
                new Label { Name = caption.Replace(" ", "") + "Value", Text = "-", ForeColor = AppTheme.Text, UseMnemonic = false }))
            .ToArray();
        foreach (var row in _rows) Controls.AddRange([row.Caption, row.Value]);
        Controls.Add(_separator);
        UpdateDetailFonts();
        ArrangeRows();
    }

    internal void Render(UsbDevice? device)
    {
        var details = DeviceDetails.From(device);
        if (_details == details) return;
        _details = details;
        SuspendLayout();
        var values = new[] { details.Product, details.Vendor, details.Description, details.Connection, details.Sharing, details.BusId, details.PersistedGuid };
        for (var i = 0; i < _rows.Length; i++) _rows[i].Value.Text = values[i];
        ResumeLayout(true);
        ArrangeRows();
    }

    internal void Toggle()
    {
        _expanded = !_expanded;
        AutoScrollPosition = Point.Empty;
        ArrangeRows();
    }

    internal void LimitHeight(int maximumHeight)
    {
        if (_maximumHeight == maximumHeight) return;
        _maximumHeight = maximumHeight;
        ArrangeRows();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        ArrangeRows();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        UpdateDetailFonts();
        ArrangeRows();
    }

    private void UpdateDetailFonts()
    {
        if (_rows is null) return;
        var oldCaption = _captionFont;
        var oldValue = _valueFont;
        var oldMono = _monoFont;
        _captionFont = new Font(Font.FontFamily, Font.SizeInPoints * 13 / 16);
        _valueFont = new Font(Font.FontFamily, Font.SizeInPoints * 15 / 16);
        _monoFont = new Font("Consolas", Font.SizeInPoints * 13 / 16);
        SuspendLayout();
        for (var i = 0; i < _rows.Length; i++)
        {
            _rows[i].Caption.Font = _captionFont;
            _rows[i].Value.Font = i == 6 ? _monoFont : _valueFont;
        }
        ResumeLayout(true);
        oldCaption?.Dispose();
        oldValue?.Dispose();
        oldMono?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        _captionFont?.Dispose();
        _valueFont?.Dispose();
        _monoFont?.Dispose();
    }

    private void ArrangeRows()
    {
        if (_rows is null || _arranging) return;
        _arranging = true;
        try
        {
            int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96d);
            var left = Scale(24);
            // Measure every caption, including hidden rows, to keep the same
            // columns in both modes while accommodating enlarged body text.
            var labelWidth = Math.Clamp(_rows.Max(row => TextRenderer.MeasureText(row.Caption.Text, row.Caption.Font).Width), Scale(105), Scale(160));
            var valueLeft = left + labelWidth + Scale(12);
            // The right inset also accommodates the scrollbar. Keep the full
            // value column in both modes instead of reserving that space twice.
            var valueWidth = Math.Max(Scale(80), Width - valueLeft - left);
            var y = Scale(20);
            for (var i = 0; i < _rows.Length; i++)
            {
                var (caption, value) = _rows[i];
                var show = _expanded || i is 0 or 6;
                caption.Visible = value.Visible = show;
                if (!show) continue;
                var rowHeight = Math.Max(caption.GetPreferredSize(new Size(labelWidth, 0)).Height,
                    value.GetPreferredSize(new Size(valueWidth, 0)).Height);
                caption.SetBounds(left, y + AutoScrollPosition.Y, labelWidth, rowHeight);
                value.SetBounds(valueLeft, y + AutoScrollPosition.Y, valueWidth, rowHeight);
                y += rowHeight + Scale(12);
                if (i == 1)
                {
                    _separator.SetBounds(left, y + AutoScrollPosition.Y, Width - left * 2, Scale(1));
                    y += Scale(17);
                }
            }
            _separator.Visible = _expanded;
            var contentHeight = y - Scale(12) + Scale(20);
            AutoScrollMinSize = new Size(0, contentHeight);
            Height = Math.Min(contentHeight, _maximumHeight);
        }
        finally { _arranging = false; }
    }
}
