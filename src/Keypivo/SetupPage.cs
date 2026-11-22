using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using Keypivo.Core;

namespace Keypivo;

internal sealed class SetupPage : FlowLayoutPanel
{
    private readonly Button _back;
    private readonly PictureBox _symbol;
    private readonly Label _title;
    private readonly Label _subtitle;
    private readonly Label _commandLabel;
    private readonly Label _command;
    private readonly Button _copy;
    private readonly LinkLabel _help;
    private readonly Label _feedback;
    private readonly Button _check;
    private ManualSetup? _setup;
    private bool _canCopy;
    private (bool CanCopy, string? Feedback, bool Busy, bool Checking)? _lastPresentation;

    public event Action? BackRequested;
    public event Action? CopyRequested;
    public event Action? CheckRequested;

    public SetupPage()
    {
        Name = "SetupPage";
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        Width = 338;
        Margin = Padding.Empty;
        BackColor = AppTheme.Surface;

        var header = Column(338, new Padding(14, 14, 14, 4));
        header.FlowDirection = FlowDirection.LeftToRight;
        _back = new Button
        {
            Name = "SetupBack", Text = "←", AccessibleName = "Back to menu", Width = 30, Height = 30,
            FlatStyle = FlatStyle.Flat, ForeColor = AppTheme.Muted, BackColor = AppTheme.Surface, Margin = Padding.Empty
        };
        _back.FlatAppearance.BorderSize = 0;
        header.Controls.Add(_back);
        header.Controls.Add(new Label { Text = "Keypivo", AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold), Margin = new Padding(8, 5, 0, 0) });
        Controls.Add(header);
        var content = Column(338, new Padding(24, 20, 24, 24));
        _symbol = new PictureBox { Size = new Size(40, 40), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(125, 0, 0, 22), TabStop = false };
        _title = TextLabel(290, new Font("Segoe UI", 14, FontStyle.Bold));
        _title.Name = "SetupTitle";
        _title.Margin = new Padding(0, 0, 0, 7);
        _subtitle = TextLabel(290);
        _subtitle.ForeColor = AppTheme.Muted;
        _subtitle.Margin = new Padding(0, 0, 0, 27);
        _commandLabel = TextLabel(290);
        _commandLabel.Margin = new Padding(0, 0, 0, 12);
        var commandBox = Column(290, new Padding(12));
        commandBox.Name = "CommandBox";
        commandBox.BackColor = AppTheme.Hover;
        commandBox.Margin = new Padding(0, 0, 0, 7);
        commandBox.Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, commandBox.ClientRectangle, AppTheme.Border, ButtonBorderStyle.Solid);
        _command = TextLabel(266, new Font("Consolas", 9.5f));
        _command.Name = "SetupCommand";
        _command.AccessibleName = "Command to run manually";
        _command.TextAlign = ContentAlignment.MiddleLeft;
        commandBox.Controls.Add(_command);
        _copy = new Button
        {
            Name = "CopyCommand", Text = "Copy command", AccessibleName = "Copy command", Width = 290, Height = 36,
            FlatStyle = FlatStyle.Flat, ForeColor = AppTheme.Accent, BackColor = AppTheme.Surface,
            Margin = new Padding(0, 0, 0, 20)
        };
        _copy.FlatAppearance.BorderSize = 0;
        _copy.FlatAppearance.MouseOverBackColor = AppTheme.Hover;
        _help = new LinkLabel
        {
            Name = "InstallationHelp", Text = "Other installation options", AccessibleName = "Other installation options (opens Microsoft Learn)",
            Width = 290, Height = 26, TextAlign = ContentAlignment.MiddleCenter,
            LinkColor = AppTheme.Muted, ActiveLinkColor = AppTheme.Accent, VisitedLinkColor = AppTheme.Muted,
            LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0, 0, 0, 20)
        };
        _feedback = TextLabel(290);
        _feedback.Name = "SetupFeedback";
        _feedback.TextAlign = ContentAlignment.MiddleLeft;
        _feedback.ForeColor = AppTheme.Muted;
        _feedback.Margin = new Padding(0, 0, 0, 18);
        _feedback.Visible = false;
        _check = new Button
        {
            Name = "CheckSetup", Width = 290, Height = 40, FlatStyle = FlatStyle.Flat,
            ForeColor = AppTheme.OnAccent, BackColor = AppTheme.Accent, Margin = Padding.Empty
        };
        _check.FlatAppearance.BorderSize = 0;
        content.Controls.AddRange([_symbol, _title, _subtitle, _commandLabel, commandBox, _copy, _help, _feedback, _check]);
        Controls.Add(content);
        _back.Click += (_, _) => BackRequested?.Invoke();
        _copy.Click += (_, _) => CopyRequested?.Invoke();
        _check.Click += (_, _) => CheckRequested?.Invoke();
        _help.LinkClicked += (_, _) => OpenHelp();
    }

    public void SetInstructions(ManualSetup setup)
    {
        _setup = setup;
        _lastPresentation = null;
        SuspendLayout();
        _title.Text = setup.Title;
        _subtitle.Text = setup.Subtitle;
        _commandLabel.Text = setup.CommandLabel;
        _command.Text = setup.Command;
        _copy.Text = "Copy command";
        _help.Visible = setup.Kind == SetupKind.Install;
        _feedback.Text = "";
        _feedback.Visible = false;
        _symbol.Image?.Dispose();
        _symbol.Image = DrawSymbol(setup.Kind);
        ResumeLayout(true);
    }

    public bool Render(SetupCheckResult result, string? feedback, bool busy, bool checking)
    {
        var presentation = (result.CanCopy, feedback, busy, checking);
        if (_lastPresentation == presentation) return false;
        _lastPresentation = presentation;
        _canCopy = result.CanCopy;
        _copy.Enabled = !busy && _canCopy;
        _back.Enabled = _check.Enabled = _help.Enabled = !busy;
        _check.Text = checking ? "Checking…" : _setup?.CheckLabel;
        _feedback.Text = feedback ?? "";
        _feedback.Visible = !string.IsNullOrWhiteSpace(feedback);
        return true;
    }

    public void MarkCopied() => _copy.Text = "Copied";
    public void FocusPrimary() { if (!_copy.Focus()) _check.Focus(); }

    private void OpenHelp()
    {
        try { Process.Start(new ProcessStartInfo(ManualSetup.InstallationHelpUrl) { UseShellExecute = true }); }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            _feedback.Text = "The installation guide could not be opened. Try again later.";
            _feedback.Visible = true;
        }
    }

    private static FlowLayoutPanel Column(int width, Padding padding) => new()
    {
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Width = width,
        MinimumSize = new Size(width, 0), MaximumSize = new Size(width, 0),
        FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = padding, Margin = Padding.Empty
    };

    private static Label TextLabel(int width, Font? font = null) => new()
    {
        AutoSize = true, MinimumSize = new Size(width, 0), MaximumSize = new Size(width, 0),
        TextAlign = ContentAlignment.MiddleCenter, Font = font ?? new Font("Segoe UI", 10),
        ForeColor = AppTheme.Text, Margin = Padding.Empty, UseMnemonic = false
    };

    private static Bitmap DrawSymbol(SetupKind kind)
    {
        var bitmap = new Bitmap(80, 80);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new SolidBrush(AppTheme.AccentSurface);
        using var pen = new Pen(AppTheme.Accent, 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.FillEllipse(background, 0, 0, 79, 79);
        if (kind == SetupKind.Install)
        {
            graphics.DrawLine(pen, 40, 22, 40, 47);
            graphics.DrawLines(pen, [new(31, 39), new(40, 48), new(49, 39)]);
            graphics.DrawLines(pen, [new(24, 47), new(24, 56), new(56, 56), new(56, 47)]);
        }
        else
        {
            graphics.DrawEllipse(pen, 38, 22, 20, 20);
            graphics.DrawLines(pen, [new(41, 39), new(23, 57), new(23, 63), new(31, 63), new(31, 56), new(38, 56), new(38, 49)]);
            if (kind == SetupKind.Unshare) graphics.DrawLine(pen, 20, 20, 60, 60);
        }
        return bitmap;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _symbol.Image?.Dispose();
        base.Dispose(disposing);
    }
}
