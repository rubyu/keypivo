using System.ComponentModel;
using System.Diagnostics;
using Keypivo.Core;

namespace Keypivo;

internal sealed class SettingsForm : Form
{
    private readonly TextBox _vid = new() { Width = 360, MaxLength = 4, CharacterCasing = CharacterCasing.Upper, AccessibleName = "Vendor ID (VID)" };
    private readonly TextBox _pid = new() { Width = 360, MaxLength = 4, CharacterCasing = CharacterCasing.Upper, PlaceholderText = "Any product", AccessibleName = "Product ID (PID)" };
    private readonly ComboBox _position = new() { Width = 360, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Display position" };
    private readonly CheckBox _startup = new() { Name = "StartAtSignIn", Text = "Start at sign-in", AutoSize = true, Enabled = false, Margin = new Padding(0, 0, 0, 8) };
    private readonly Label _startupHint = new() { Name = "StartupHint", Text = "Checking startup setting…", AutoSize = true, MaximumSize = new Size(360, 0), ForeColor = AppTheme.Muted, Margin = new Padding(0, 0, 0, 8) };
    private readonly LinkLabel _startupSettings = new() { Name = "WindowsStartupSettings", Text = "Open Windows startup settings", AutoSize = true, Visible = false, Margin = new Padding(0, 0, 0, 12) };
    private readonly Label _error = new() { Name = "SettingsError", AutoSize = true, MaximumSize = new Size(360, 0), ForeColor = AppTheme.Error, Visible = false, Margin = new Padding(0, 12, 0, 0) };
    private readonly Button _save = new() { Name = "SaveSettings", Text = "Save", AutoSize = true, Enabled = false, MinimumSize = new Size(90, 36) };
    private readonly LinkLabel _unshare = new() { Name = "UnshareKey", Text = "Stop sharing this key", AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
    private readonly FlowLayoutPanel _content;
    private readonly AppSettings _original;
    private readonly bool _canUnshare;
    private readonly Func<AppSettings, Task> _apply;
    private readonly IStartupService _startupService;
    private bool _initialStartupEnabled;
    private bool _loadingStartup;
    private bool _reloadStartupOnActivate;
    private bool _saving;

    public SettingsForm(AppSettings settings, bool canUnshare, bool isAttached, Func<AppSettings, Task> apply, IStartupService? startupService = null)
    {
        _original = settings;
        _canUnshare = canUnshare && !isAttached;
        _apply = apply;
        _startupService = startupService ?? new StartupService();
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
        _content.Controls.AddRange([_startup, _startupHint, _startupSettings]);
        _content.Controls.Add(_unshare);
        if (canUnshare && isAttached)
            _content.Controls.Add(new Label { Text = "Switch to Windows first.", AutoSize = true, ForeColor = AppTheme.Muted, Margin = new Padding(0, 2, 0, 0) });
        _content.Controls.Add(_error);
        var buttons = new FlowLayoutPanel
        {
            Width = 360, MinimumSize = new Size(360, 0), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0, 22, 0, 0)
        };
        var cancel = new Button { Name = "CancelSettings", Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(90, 36) };
        buttons.Controls.AddRange([_save, cancel]);
        _content.Controls.Add(buttons);
        Controls.Add(_content);
        AcceptButton = _save;
        CancelButton = cancel;

        _vid.TextChanged += (_, _) => UpdateUnshare();
        _pid.TextChanged += (_, _) => UpdateUnshare();
        _position.SelectedIndexChanged += (_, _) => UpdateUnshare();
        _startup.CheckedChanged += (_, _) => UpdateUnshare();
        _startupSettings.LinkClicked += (_, _) => OpenStartupSettings();
        _save.Click += async (_, _) => await SaveAsync();
        _unshare.LinkClicked += (_, _) => { DialogResult = DialogResult.Retry; Close(); };
        Shown += (_, _) => Place(settings.Position);
        Shown += async (_, _) => await LoadStartupAsync();
        Activated += async (_, _) =>
        {
            if (!_reloadStartupOnActivate || _saving) return;
            _reloadStartupOnActivate = false;
            await LoadStartupAsync();
        };
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
        _unshare.Enabled = _canUnshare && !_saving && !_loadingStartup && ReadSettings() == _original
            && _startup.Checked == _initialStartupEnabled;
    }

    private async Task LoadStartupAsync()
    {
        if (_loadingStartup) return;
        _loadingStartup = true;
        _save.Enabled = _startup.Enabled = false;
        UpdateUnshare();
        StartupStatus status;
        try { status = await _startupService.GetStatusAsync(); }
        catch (Exception) { status = StartupStatus.Unavailable; }
        if (IsDisposed || Disposing) return;
        _loadingStartup = false;
        ShowStartupStatus(status);
        _save.Enabled = true;
    }

    private void ShowStartupStatus(StartupStatus status)
    {
        _initialStartupEnabled = status.IsEnabled();
        _startup.Checked = _initialStartupEnabled;
        _startup.Enabled = status.CanChange();
        _startupHint.Text = status.Hint() ?? "";
        _startupHint.Visible = !string.IsNullOrEmpty(_startupHint.Text);
        _startupSettings.Visible = status == StartupStatus.DisabledByUser;
        UpdateUnshare();
    }

    private void OpenStartupSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });
            _reloadStartupOnActivate = true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            _error.Text = "Windows startup settings could not be opened. Try again.";
            _error.Visible = true;
        }
    }

    private async Task SaveAsync()
    {
        if (_saving || _loadingStartup) return;
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
            if (_startup.Checked != _initialStartupEnabled)
            {
                var requested = _startup.Checked;
                StartupStatus status;
                try { status = await _startupService.SetEnabledAsync(requested); }
                catch (Exception)
                {
                    await LoadStartupAsync();
                    _error.Text = "Other settings were saved. Startup could not be updated. Try again.";
                    _error.Visible = true;
                    return;
                }
                ShowStartupStatus(status);
                if (status is StartupStatus.Unavailable or StartupStatus.Unpackaged || status.IsEnabled() != requested)
                {
                    _error.Text = "Other settings were saved. The startup setting could not be changed.";
                    _error.Visible = true;
                    return;
                }
            }
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
