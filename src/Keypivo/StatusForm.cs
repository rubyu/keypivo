using System.ComponentModel;
using System.Runtime.InteropServices;
using Keypivo.Core;

namespace Keypivo;

internal sealed class StatusForm : Form
{
    private readonly FlowLayoutPanel _layout;
    private readonly SetupPage _setupPage = new() { Visible = false, Location = new Point(1, 1) };
    private readonly PictureBox _logo = new() { Size = new(22, 22), SizeMode = PictureBoxSizeMode.Zoom, Margin = new(0, 0, 8, 0) };
    private readonly Label _status;
    private readonly Button _switch = MenuButton("Switch to WSL", primary: true);
    private readonly Button _refresh = MenuButton("Refresh status");
    private readonly Button _settings = MenuButton("Settings");
    private readonly Button _exit = MenuButton("Exit");
    private readonly Button _details = MenuButton("View details");
    private Icon? _lastIcon;
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
        Text = "Keypivo";
        Font = new Font("Segoe UI", 10);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = AppTheme.Surface;
        ForeColor = AppTheme.Text;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(1);

        _layout = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Location = new Point(1, 1), Margin = Padding.Empty, Padding = new Padding(0, 0, 0, 5)
        };
        var header = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = 318, MinimumSize = new Size(318, 0), Margin = Padding.Empty,
            Padding = new Padding(16, 15, 16, 10), WrapContents = false
        };
        header.Controls.Add(_logo);
        header.Controls.Add(new Label
        {
            Text = "Keypivo", AutoSize = true, Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 1, 0, 0)
        });
        var state = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(318, 0), FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Margin = Padding.Empty, Padding = new Padding(18, 8, 18, 15)
        };
        state.Controls.Add(new Label
        {
            Text = "Security key", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = Padding.Empty
        });
        _status = new Label
        {
            Text = "Checking…", AutoSize = true, MaximumSize = new Size(282, 0),
            ForeColor = AppTheme.Muted, Margin = new Padding(0, 3, 0, 0)
        };
        state.Controls.Add(_status);
        _details.Visible = false;
        _switch.Name = "SwitchAction";
        _details.Name = "StatusDetails";
        _details.ForeColor = AppTheme.Error;
        _layout.Controls.AddRange([header, state, _details, Separator(), _switch, _refresh, _settings, Separator(), _exit]);
        Controls.AddRange([_layout, _setupPage]);

        _switch.Click += (_, _) => SwitchRequested?.Invoke();
        _refresh.Click += (_, _) => RefreshRequested?.Invoke();
        _settings.Click += (_, _) => SettingsRequested?.Invoke();
        _exit.Click += (_, _) => ExitRequested?.Invoke();
        _details.Click += (_, _) => DetailsRequested?.Invoke();
        _setupPage.BackRequested += () => SetupBackRequested?.Invoke();
        _setupPage.CopyRequested += () => SetupCopyRequested?.Invoke();
        _setupPage.CheckRequested += () => SetupCheckRequested?.Invoke();
        Deactivate += (_, _) => { if (!KeepOpen && !IsSetupVisible) Hide(); };
        SizeChanged += (_, _) => { if (Visible) Place(); };
    }

    public void Render(DeviceSnapshot snapshot, bool busy, bool refreshing, string? error, Icon icon, bool unconfirmed = false)
    {
        SuspendLayout();
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
        _setupPage.Render(result, feedback, busy, checking);
        PerformLayout();
        if (Visible) Place();
    }

    public void MarkCommandCopied() => _setupPage.MarkCopied();

    public void ShowAt(DisplayPosition position)
    {
        _position = position;
        Place();
        if (!Visible) Show();
        Place();
        Activate();
        if (IsSetupVisible) _setupPage.FocusPrimary();
        else if (!_switch.Focus()) _refresh.Focus();
    }

    private void Place()
    {
        var area = (Screen.PrimaryScreen ?? Screen.FromControl(this)).WorkingArea;
        Location = WindowPlacement.Calculate(area, Size, _position, (int)Math.Round(16 * DeviceDpi / 96d));
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
        if (disposing) _logo.Image?.Dispose();
        base.Dispose(disposing);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private static Button MenuButton(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text, Width = 308, Height = 42, Margin = new Padding(5, 0, 5, 0),
            FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 12, 0), TabStop = true,
            BackColor = primary ? AppTheme.AccentSurface : AppTheme.Surface,
            ForeColor = AppTheme.Text, UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = primary ? AppTheme.AccentHover : AppTheme.Hover;
        button.FlatAppearance.MouseDownBackColor = AppTheme.AccentHover;
        return button;
    }

    private static Panel Separator() => new()
    {
        BackColor = AppTheme.Border, Height = 1, Width = 318, Margin = new Padding(0, 5, 0, 5)
    };
}
