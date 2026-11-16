using Keypivo.Core;

namespace Keypivo;

internal sealed class SettingsForm : Form
{
    private readonly TextBox _vid = new() { Width = 360, MaxLength = 4, CharacterCasing = CharacterCasing.Upper, AccessibleName = "Vendor ID (VID)" };
    private readonly TextBox _pid = new() { Width = 360, MaxLength = 4, CharacterCasing = CharacterCasing.Upper, PlaceholderText = "Any product", AccessibleName = "Product ID (PID)" };
    private readonly ComboBox _position = new() { Width = 360, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Display position" };
    private readonly Label _error = new() { AutoSize = true, MaximumSize = new Size(360, 0), ForeColor = AppTheme.Error, Visible = false, Margin = new Padding(0, 12, 0, 0) };
    private readonly Button _save = new() { Text = "Save", AutoSize = true, MinimumSize = new Size(90, 36) };
    private readonly LinkLabel _unshare = new() { Text = "Stop sharing this key", AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
    private readonly FlowLayoutPanel _content;
    private readonly AppSettings _original;
    private readonly bool _canUnshare;
    private readonly Func<AppSettings, Task> _apply;
    private bool _saving;

    public SettingsForm(AppSettings settings, bool canUnshare, Func<AppSettings, Task> apply)
    {
        _original = settings;
        _canUnshare = canUnshare;
        _apply = apply;
        Text = "Settings — Keypivo";
        Font = new Font("Segoe UI", 10);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = AppTheme.Surface;
        ForeColor = AppTheme.Text;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _content = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(24), Margin = Padding.Empty, Location = Point.Empty
        };
        _vid.Text = settings.VendorId;
        _pid.Text = settings.ProductId;
        _position.Items.AddRange(["Bottom right", "Center"]);
        _position.SelectedIndex = settings.Position == DisplayPosition.Center ? 1 : 0;
        AddField("Vendor ID (VID)", _vid);
        AddField("Product ID (PID)", _pid);
        AddField("Display position", _position);
        _content.Controls.Add(_unshare);
        _content.Controls.Add(_error);
        var buttons = new FlowLayoutPanel
        {
            Width = 360, MinimumSize = new Size(360, 0), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0, 22, 0, 0)
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(90, 36) };
        buttons.Controls.AddRange([_save, cancel]);
        _content.Controls.Add(buttons);
        Controls.Add(_content);
        AcceptButton = _save;
        CancelButton = cancel;

        _vid.TextChanged += (_, _) => UpdateUnshare();
        _pid.TextChanged += (_, _) => UpdateUnshare();
        _position.SelectedIndexChanged += (_, _) => UpdateUnshare();
        _save.Click += async (_, _) => await SaveAsync();
        _unshare.LinkClicked += (_, _) => { DialogResult = DialogResult.Retry; Close(); };
        Shown += (_, _) => Place(settings.Position);
        SizeChanged += (_, _) => { if (Visible) Place(settings.Position); };
        UpdateUnshare();
    }

    private void AddField(string caption, Control control)
    {
        _content.Controls.Add(new Label { Text = caption, AutoSize = true, Margin = new Padding(0, 0, 0, 6) });
        control.Margin = new Padding(0, 0, 0, 18);
        _content.Controls.Add(control);
    }

    private AppSettings ReadSettings() => new()
    {
        VendorId = _vid.Text,
        ProductId = _pid.Text,
        Position = _position.SelectedIndex == 1 ? DisplayPosition.Center : DisplayPosition.BottomRight
    };

    private void UpdateUnshare()
    {
        // Never operate on the old key while the dialog shows an unsaved new filter.
        _unshare.Enabled = _canUnshare && !_saving && ReadSettings() == _original;
    }

    private async Task SaveAsync()
    {
        AppSettings settings;
        try { settings = ReadSettings().Normalize(); }
        catch (ArgumentException exception)
        {
            var vendor = exception.ParamName == "vendorId";
            _error.Text = vendor ? "Enter a 4-digit hexadecimal VID."
                : "Enter a 4-digit hexadecimal PID, or leave it empty.";
            _error.Visible = true;
            var field = vendor ? _vid : _pid;
            field.Focus();
            field.SelectAll();
            return;
        }
        _saving = true;
        _content.Enabled = false;
        _error.Visible = false;
        try
        {
            await _apply(settings);
            _saving = false;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            _error.Text = "Settings could not be saved. Check file permissions and try again.";
            _error.Visible = true;
        }
        finally
        {
            _saving = false;
            _content.Enabled = true;
            UpdateUnshare();
        }
    }

    private void Place(DisplayPosition position)
    {
        var area = (Screen.PrimaryScreen ?? Screen.FromControl(this)).WorkingArea;
        Location = WindowPlacement.Calculate(area, Size, position, (int)Math.Round(16 * DeviceDpi / 96d));
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_saving && e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
        base.OnFormClosing(e);
    }
}
