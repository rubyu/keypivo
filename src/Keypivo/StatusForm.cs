using System.ComponentModel;
using System.Runtime.InteropServices;
using Keypivo.Core;

namespace Keypivo;

internal sealed class StatusForm : Form
{
    private readonly FlowLayoutPanel _layout;
    private readonly SetupPage _setupPage = new() { Visible = false, Location = new Point(1, 1) };
    private readonly PictureBox _logo = new() { Size = new(23, 23), SizeMode = PictureBoxSizeMode.Zoom, Margin = new(0, 0, 10, 0), TabStop = false };
    private readonly Label _status;
    private readonly Label _deviceHeading;
    private readonly Button _switch = new PopupButton { Text = "Switch to WSL", Margin = new Padding(24, 0, 24, 0) };
    private readonly Button _refresh = new PopupButton(PopupGlyph.Refresh) { Name = "RefreshStatus", AccessibleName = "Refresh status", Margin = new Padding(0, 0, 4, 0) };
    private readonly Button _settings = new PopupButton(PopupGlyph.Settings) { Name = "OpenSettings", AccessibleName = "Settings", Margin = new Padding(0, 0, 4, 0) };
    private readonly Button _exit = new PopupButton(PopupGlyph.Power) { Name = "ExitApplication", AccessibleName = "Exit Keypivo" };
    private readonly ToolTip _toolTip = new();
    private readonly PopupButton _info = new(PopupGlyph.Info) { Name = "ToggleDeviceDetails", AccessibleName = "Show all device details", Margin = new Padding(0, 0, 4, 0) };
    private readonly CopyFeedback _copyFeedback = new();
    private readonly DeviceDetailsPanel _deviceDetails;
    private bool _sizingDetails;
    private readonly Button _details = MenuButton("View details");
    private Icon? _lastIcon;
    private (DeviceSnapshot Snapshot, bool Busy, bool Refreshing, string? Error, Icon Icon, bool Unconfirmed)? _lastPresentation;
    private DisplayPosition _position;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AllowClose { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool KeepOpen { get; set; }
    public event Action? SwitchRequested;
    public event Action? RefreshRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;
    public event Action? DetailsRequested;
    public event Action? SetupBackRequested;
    public event Action? SetupCopyRequested;
    public event Action? SetupCheckRequested;
    internal bool IsSetupVisible => _setupPage.Visible;

    public StatusForm()
    {
        // Defer the initial DPI pass until every child has its logical bounds.
        SuspendLayout();
        _deviceDetails = new DeviceDetailsPanel(_copyFeedback);
        Text = "Keypivo";
        Font = new Font("Segoe UI", 12);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = AppTheme.Surface;
        ForeColor = AppTheme.Text;
        DoubleBuffered = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(1);

        _layout = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Name = "MenuLayout", Location = new Point(1, 1), Margin = Padding.Empty
        };
        var header = new TableLayoutPanel
        {
            Name = "MenuHeader", ColumnCount = 2, RowCount = 1,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = PopupLayout.Width, MinimumSize = new Size(PopupLayout.Width, 0), Margin = Padding.Empty,
            Padding = new Padding(22, 12, 14, 12), BackColor = AppTheme.Chrome
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var brand = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
            Anchor = AnchorStyles.Left, Margin = Padding.Empty
        };
        brand.Controls.Add(_logo);
        brand.Controls.Add(new Label
        {
            Text = "Keypivo", AutoSize = true, Font = new Font(Font.FontFamily, 12, FontStyle.Bold),
            Margin = new Padding(0, 1, 0, 0)
        });
        var tools = new FlowLayoutPanel
        {
            Name = "HeaderActions", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty
        };
        tools.Controls.AddRange([_refresh, _info, _settings, _exit]);
        foreach (Control button in tools.Controls) _toolTip.SetToolTip(button, button.AccessibleName);
        header.Controls.Add(brand, 0, 0);
        header.Controls.Add(tools, 1, 0);
        var state = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(PopupLayout.Width, 0), FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Margin = Padding.Empty, Padding = new Padding(24, 32, 24, 32)
        };
        _deviceHeading = new CopyableLabel(_copyFeedback)
        {
            Name = "DeviceHeading", Text = "-", Font = new Font(Font.FontFamily, 14, FontStyle.Bold),
            Width = PopupLayout.ContentWidth, Height = 40, AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        _deviceHeading.FontChanged += (_, _) => SizeDeviceHeading();
        _deviceHeading.SizeChanged += (_, _) => SizeDeviceHeading();
        state.Controls.Add(_deviceHeading);
        _status = new Label
        {
            Name = "ConnectionStatus", Text = "Checking…", AutoSize = true, MaximumSize = new Size(PopupLayout.ContentWidth, 0),
            ForeColor = AppTheme.Muted, Margin = new Padding(0, 6, 0, 0)
        };
        state.Controls.Add(_status);
        _details.Visible = false;
        _switch.Name = "SwitchAction";
        _details.Name = "StatusDetails";
        _details.ForeColor = AppTheme.Error;
        _layout.Controls.AddRange([header, Separator(), state, _details, _switch, _deviceDetails]);
        Controls.AddRange([_layout, _setupPage]);

        _switch.Click += (_, _) => SwitchRequested?.Invoke();
        _refresh.Click += (_, _) => RefreshRequested?.Invoke();
        _settings.Click += (_, _) => SettingsRequested?.Invoke();
        _exit.Click += (_, _) => ExitRequested?.Invoke();
        _details.Click += (_, _) => DetailsRequested?.Invoke();
        _info.Click += (_, _) =>
        {
            _deviceDetails.Toggle();
            _info.Selected = _deviceDetails.Expanded;
            _info.AccessibleName = _deviceDetails.Expanded ? "Show compact device details" : "Show all device details";
            _toolTip.SetToolTip(_info, _info.AccessibleName);
        };
        _setupPage.BackRequested += () => SetupBackRequested?.Invoke();
        _setupPage.CopyRequested += () => SetupCopyRequested?.Invoke();
        _setupPage.CheckRequested += () => SetupCheckRequested?.Invoke();
        Deactivate += (_, _) =>
        {
            // Instructions may stay open behind PowerShell, but must not remain above it.
            TopMost = false;
            if (!KeepOpen && !IsSetupVisible) Hide();
        };
        VisibleChanged += (_, _) =>
        {
            if (!Visible)
            {
                TopMost = false;
                _copyFeedback.Hide();
            }
        };
        SizeChanged += (_, _) => { if (Visible) Place(); };
        ResumeLayout(true);
    }

    public void Render(DeviceSnapshot snapshot, bool busy, bool refreshing, string? error, Icon icon, bool unconfirmed = false)
    {
        var presentation = (snapshot, busy, refreshing, error, icon, unconfirmed);
        if (_lastPresentation == presentation) return;
        _lastPresentation = presentation;
        SuspendLayout();
        var description = snapshot.Device?.Description;
        _deviceHeading.Text = string.IsNullOrWhiteSpace(description) ? "-" : description;
        SizeDeviceHeading();
        _status.Text = busy ? "Switching…" : StatePresentation.Title(snapshot.State);
        _switch.Text = busy ? "Switching…" : snapshot.State == ConnectionState.ToolMissing ? "Set up…"
            : snapshot.Device?.IsAttached == true ? "Switch to Windows"
            : snapshot.Device is { IsShared: false } ? "Set up for WSL…" : "Switch to WSL";
        _switch.Enabled = !busy && !refreshing && (snapshot.CanOperate || snapshot.State == ConnectionState.ToolMissing);
        _refresh.Enabled = !busy && !refreshing;
        _settings.Enabled = !busy && !refreshing;
        _exit.Enabled = !busy;
        _details.Visible = snapshot.State != ConnectionState.ToolMissing && !string.IsNullOrEmpty(error ?? snapshot.Error);
        _details.Text = error is null ? "View details" : unconfirmed ? "Not confirmed · View details" : "Action failed · View details";
        _details.ForeColor = unconfirmed ? AppTheme.Muted : AppTheme.Error;
        _deviceDetails.Render(snapshot.Device);
        if (!ReferenceEquals(_lastIcon, icon))
        {
            _logo.Image?.Dispose();
            _logo.Image = icon.ToBitmap();
            Icon = icon;
            _lastIcon = icon;
        }
        ResumeLayout(performLayout: true);
        if (Visible) Place();
    }

    public void ShowSetup(ManualSetup setup)
    {
        SuspendLayout();
        _setupPage.SetInstructions(setup);
        _layout.Visible = false;
        _setupPage.Visible = true;
        ResumeLayout(true);
        if (Visible) Place();
    }

    public void ShowMenu()
    {
        SuspendLayout();
        _setupPage.Visible = false;
        _layout.Visible = true;
        ResumeLayout(true);
        if (Visible) { Place(); _switch.Focus(); }
    }

    public void RenderSetup(SetupCheckResult result, string? feedback, bool busy, bool checking)
    {
        if (!_setupPage.Render(result, feedback, busy, checking)) return;
        PerformLayout();
        if (Visible) Place();
    }

    public void MarkCommandCopied() => _setupPage.MarkCopied();

    public void ShowAt(DisplayPosition position)
    {
        _position = position;
        Place();
        // The notification-area overflow is itself topmost. Raise the popup into
        // that band on explicit opening; deactivation and hiding release it.
        TopMost = true;
        if (!Visible) Show();
        Place();
        BringToFront();
        Activate();
        if (IsSetupVisible) _setupPage.FocusPrimary();
        else if (!_switch.Focus()) _refresh.Focus();
    }

    private void Place()
    {
        var area = (Screen.PrimaryScreen ?? Screen.FromControl(this)).WorkingArea;
        Location = WindowPlacement.Calculate(area, Size, _position, (int)Math.Round(16 * DeviceDpi / 96d));
    }

    private void SizeDeviceHeading()
    {
        // Keep the action visible even for unusually long Windows descriptions.
        // The full description remains available in the tooltip and details.
        var preferred = _deviceHeading.GetPreferredSize(new Size(_deviceHeading.Width, 0)).Height;
        var threeLines = TextRenderer.MeasureText("Ag\nAg\nAg", _deviceHeading.Font,
            new Size(_deviceHeading.Width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        _deviceHeading.Height = Math.Min(preferred, threeLines);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_layout is null || _sizingDetails) return;
        _sizingDetails = true;
        try
        {
            var area = (Screen.PrimaryScreen ?? Screen.FromControl(this)).WorkingArea;
            var available = area.Height - (int)Math.Round(32 * DeviceDpi / 96d) - (_layout.Height - _deviceDetails.Height) - 2;
            _deviceDetails.LimitHeight(Math.Max(80, available));
        }
        finally { _sizingDetails = false; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        ControlPaint.DrawBorder(e.Graphics, ClientRectangle, AppTheme.Border, ButtonBorderStyle.Solid);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Windows 11 can round the popup; earlier versions ignore this attribute.
        var preference = 3;
        _ = DwmSetWindowAttribute(Handle, 33, ref preference, sizeof(int));
    }

    protected override CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; parameters.ClassStyle |= 0x00020000; return parameters; }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Hide(); return true; }
        if (keyData is Keys.Down or Keys.Up)
        {
            SelectNextControl(ActiveControl, keyData == Keys.Down, true, true, true);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!AllowClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
            _copyFeedback.Dispose();
            _logo.Image?.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private static Button MenuButton(string text)
    {
        var button = new Button
        {
            Text = text, Width = PopupLayout.Width - 16, Height = 56, Margin = new Padding(8, 0, 8, 0),
            FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 0, 16, 0), TabStop = true,
            BackColor = AppTheme.Surface,
            ForeColor = AppTheme.Text, UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = AppTheme.Hover;
        button.FlatAppearance.MouseDownBackColor = AppTheme.AccentHover;
        return button;
    }

    private static Panel Separator() => new()
    {
        BackColor = AppTheme.Border, Height = 1, Width = PopupLayout.Width, Margin = Padding.Empty
    };
}
