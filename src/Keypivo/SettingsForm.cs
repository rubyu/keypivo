using System.ComponentModel;
using System.Diagnostics;
using Keypivo.Core;

namespace Keypivo;

internal sealed class SettingsForm : Form
{
    private readonly TextBox _vid = new() { Width = 360, MaxLength = 4, CharacterCasing = CharacterCasing.Upper, AccessibleName = "Vendor ID (VID)" };
    private readonly TextBox _pid = new() { Width = 360, MaxLength = 4, CharacterCasing = CharacterCasing.Upper, PlaceholderText = "Any product", AccessibleName = "Product ID (PID)" };
    // Let flow layout measure the native height after font/DPI changes instead
    // of reusing the original fixed height (ComboBox ignores minimum height).
    private readonly ComboBox _position = new() { Name = "DisplayPosition", Width = 360, AutoSize = true, DropDownStyle = ComboBoxStyle.DropDownList, FormattingEnabled = true, AccessibleName = "Display position" };
    private readonly CheckBox _startup = new() { Name = "StartAtSignIn", Text = "Start at sign-in", AutoSize = true, Enabled = false, Margin = new Padding(0, 0, 0, 8) };
    private readonly Label _startupHint = new() { Name = "StartupHint", Text = "Checking startup setting…", AutoSize = true, MaximumSize = new Size(360, 0), ForeColor = AppTheme.Muted, Margin = new Padding(0, 0, 0, 8) };
    private readonly LinkLabel _startupSettings = new() { Name = "WindowsStartupSettings", Text = "Open Windows startup settings", AutoSize = true, Visible = false, Margin = new Padding(0, 0, 0, 12) };
    private readonly Label _error = new() { Name = "SettingsError", AutoSize = true, MaximumSize = new Size(572, 0), ForeColor = AppTheme.Error, Visible = false, Margin = new Padding(0, 0, 0, 12) };
    private readonly Button _save = new() { Name = "SaveSettings", Text = "Save", AutoSize = true, Enabled = false, MinimumSize = new Size(90, 36) };
    private readonly LinkLabel _unshare = new() { Name = "UnshareKey", Text = "Stop sharing this key", AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
    private readonly TableLayoutPanel _content;
    private readonly FlowLayoutPanel _general;
    private readonly Panel _pages = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly Dictionary<string, (Button Navigation, FlowLayoutPanel Page)> _categories = new();
    private readonly Dictionary<NotificationKind, NotificationToggle> _notificationChecks = new();
    private readonly List<NotificationToggle> _notificationMasters = new();
    private bool _syncingNotifications;
    private bool _layingOutPages;
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
        Font = new Font("Segoe UI", 11);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = AppTheme.Surface;
        ForeColor = AppTheme.Text;
        ClientSize = new Size(800, 640);
        DoubleBuffered = true;

        _content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty
        };
        _content.ColumnStyles.Add(new(SizeType.Absolute, 220));
        _content.ColumnStyles.Add(new(SizeType.Percent, 100));
        _content.RowStyles.Add(new(SizeType.Percent, 100));
        _content.RowStyles.Add(new(SizeType.AutoSize));
        var navigation = new FlowLayoutPanel
        {
            Name = "SettingsNavigation", Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, BackColor = AppTheme.Hover,
            Padding = new Padding(12, 24, 12, 12), Margin = Padding.Empty
        };
        _content.Controls.Add(navigation, 0, 0);
        _content.Controls.Add(_pages, 1, 0);
        _general = AddCategory(navigation, "General", "General");
        _vid.Text = settings.VendorId;
        _pid.Text = settings.ProductId;
        _position.Items.AddRange(["Bottom right", "Center"]);
        _position.SelectedIndex = settings.Position == DisplayPosition.Center ? 1 : 0;
        AddField("Vendor ID (VID)", _vid);
        AddField("Product ID (PID)", _pid);
        AddField("Display position", _position);
        _general.Controls.AddRange([_startup, _startupHint, _startupSettings]);
        _general.Controls.Add(_unshare);
        if (canUnshare && isAttached)
            _general.Controls.Add(new Label { Text = "Switch to Windows first.", AutoSize = true, ForeColor = AppTheme.Muted, Margin = new Padding(0, 2, 0, 0) });
        navigation.Controls.Add(new Label
        {
            Name = "NotificationsHeading", Text = "Notifications", AutoSize = true, ForeColor = AppTheme.Muted,
            Margin = new Padding(10, 24, 0, 10)
        });
        AddNotifications(navigation, "Launch", "At launch", settings.Notifications,
            (NotificationKind.CurrentStatusAtLaunch, "Current status at launch"));
        AddNotifications(navigation, "Connection", "Connection", settings.Notifications,
            (NotificationKind.AvailableOnWindows, "Available on Windows"),
            (NotificationKind.ConnectedToWsl, "Connected to WSL"),
            (NotificationKind.KeyDisconnected, "Key disconnected"),
            (NotificationKind.WaitingForConnection, "Waiting for connection"));
        AddNotifications(navigation, "Attention", "Attention", settings.Notifications,
            (NotificationKind.SetupRequired, "Setup required"),
            (NotificationKind.StatusUnavailable, "Status unavailable"),
            (NotificationKind.ActionFailed, "Action failed"),
            (NotificationKind.ChangeNotConfirmed, "Change not confirmed"),
            (NotificationKind.SettingsCouldNotBeLoaded, "Settings could not be loaded"));
        navigation.Layout += (_, _) =>
        {
            foreach (var button in navigation.Controls.OfType<Button>())
                button.Size = new Size(navigation.ClientSize.Width - navigation.Padding.Horizontal,
                    Math.Max(LogicalToDeviceUnits(48), button.Font.Height + LogicalToDeviceUnits(20)));
        };
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, RowCount = 2, Padding = new Padding(28, 16, 28, 24), Margin = Padding.Empty
        };
        footer.ColumnStyles.Add(new(SizeType.Percent, 100));
        footer.RowStyles.Add(new(SizeType.AutoSize));
        footer.RowStyles.Add(new(SizeType.AutoSize));
        _error.Dock = DockStyle.Fill;
        _error.MaximumSize = Size.Empty;
        footer.Controls.Add(_error, 0, 0);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty
        };
        _save.MinimumSize = new Size(104, 44);
        var cancel = new Button { Name = "CancelSettings", Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(104, 44) };
        buttons.Controls.AddRange([_save, cancel]);
        footer.Controls.Add(buttons, 0, 1);
        _content.Controls.Add(footer, 0, 1);
        _content.SetColumnSpan(footer, 2);
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
        SelectCategory("General");
        UpdateUnshare();
    }

    private FlowLayoutPanel AddCategory(FlowLayoutPanel navigation, string id, string title)
    {
        var button = new Button
        {
            Name = "Settings" + id, Text = title, Width = 196, Height = 48, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 12, 0), Margin = new Padding(0, 0, 0, 4), FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => SelectCategory(id);
        navigation.Controls.Add(button);
        var page = new FlowLayoutPanel
        {
            Name = id + "Page", Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, BackColor = AppTheme.Surface,
            Padding = new Padding(32), Margin = Padding.Empty, Visible = false
        };
        page.Controls.Add(new Label
        {
            Text = title, AutoSize = true, Font = new Font(Font.FontFamily, 18, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 24)
        });
        _pages.Controls.Add(page);
        _categories.Add(id, (button, page));
        page.Layout += (_, _) => SizePageContents(page);
        return page;
    }

    private void SizePageContents(FlowLayoutPanel page)
    {
        if (_layingOutPages) return;
        _layingOutPages = true;
        try
        {
            var width = Math.Max(1, page.ClientSize.Width - page.Padding.Horizontal);
            foreach (Control control in page.Controls)
            {
                var available = Math.Max(1, width - control.Margin.Horizontal);
                if (control is Label || control is CheckBox { AutoSize: true })
                    control.MaximumSize = new Size(available, 0);
                else if (control is NotificationToggle toggle)
                    toggle.Size = toggle.GetPreferredSize(new Size(available, 0));
                else
                    control.Width = available;
            }
        }
        finally { _layingOutPages = false; }
    }

    private void SelectCategory(string id)
    {
        _pages.SuspendLayout();
        foreach (var (key, category) in _categories)
        {
            var selected = key == id;
            category.Page.Visible = selected;
            category.Navigation.BackColor = selected ? AppTheme.AccentSurface : AppTheme.Hover;
            category.Navigation.ForeColor = selected ? AppTheme.Accent : AppTheme.Text;
            category.Navigation.AccessibleDescription = selected ? "Selected" : "";
        }
        _categories[id].Page.BringToFront();
        _pages.ResumeLayout();
    }

    private void AddNotifications(FlowLayoutPanel navigation, string id, string title, NotificationSettings settings,
        params (NotificationKind Kind, string Label)[] items)
    {
        var page = AddCategory(navigation, id, title);
        var master = new NotificationToggle
        {
            Name = "ShowNotifications" + id, Text = "Show notifications", Checked = settings.Enabled,
            Margin = new Padding(0, 0, 0, 12)
        };
        _notificationMasters.Add(master);
        page.Controls.Add(master);
        page.Controls.Add(new Panel { Width = 360, Height = 1, BackColor = AppTheme.Border, Margin = new Padding(0, 0, 0, 12) });
        master.CheckedChanged += (_, _) =>
        {
            if (_syncingNotifications) return;
            _syncingNotifications = true;
            foreach (var other in _notificationMasters) other.Checked = master.Checked;
            foreach (var item in _notificationChecks.Values) item.Enabled = master.Checked;
            _syncingNotifications = false;
            UpdateUnshare();
        };
        foreach (var (kind, label) in items)
        {
            var toggle = new NotificationToggle
            {
                Name = "Notify" + kind, Text = label, Checked = settings.IsSelected(kind), Enabled = settings.Enabled
            };
            _notificationChecks.Add(kind, toggle);
            toggle.CheckedChanged += (_, _) => UpdateUnshare();
            page.Controls.Add(toggle);
        }
    }

    private void AddField(string caption, Control control)
    {
        _general.Controls.Add(new Label { Text = caption, AutoSize = true, Margin = new Padding(0, 0, 0, 6) });
        control.Margin = new Padding(0, 0, 0, 18);
        _general.Controls.Add(control);
    }

    private AppSettings ReadSettings() => new()
    {
        VendorId = _vid.Text,
        ProductId = _pid.Text,
        Position = _position.SelectedIndex == 1 ? DisplayPosition.Center : DisplayPosition.BottomRight,
        Notifications = _notificationChecks.Aggregate(new NotificationSettings { Enabled = _notificationMasters[0].Checked },
            (settings, pair) => settings.WithSelection(pair.Key, pair.Value.Checked))
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
            SelectCategory("General");
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
                    SelectCategory("General");
                    return;
                }
                ShowStartupStatus(status);
                if (status is StartupStatus.Unavailable or StartupStatus.Unpackaged || status.IsEnabled() != requested)
                {
                    _error.Text = "Other settings were saved. The startup setting could not be changed.";
                    _error.Visible = true;
                    SelectCategory("General");
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
        var margin = LogicalToDeviceUnits(16);
        // Keep the footer reachable on small displays and at high DPI. Pages
        // scroll vertically when the available work area cannot fit the content.
        var available = new Size(Math.Max(1, area.Width - 2 * margin), Math.Max(1, area.Height - 2 * margin));
        if (Width > available.Width || Height > available.Height)
            Size = new Size(Math.Min(Width, available.Width), Math.Min(Height, available.Height));
        Location = WindowPlacement.Calculate(area, Size, position, margin);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_saving && e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
        base.OnFormClosing(e);
    }
}
