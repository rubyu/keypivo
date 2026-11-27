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
    private readonly FlowLayoutPanel _commandBox;
    private readonly Button _copy;
    private readonly LinkLabel _help;
    private readonly Label _feedback;
    private readonly Button _done;
    private ManualSetup? _setup;
    private bool _complete;
    private (bool Complete, bool CanCopy, string? Feedback, bool Busy)? _lastPresentation;

    public event Action? BackRequested;
    public event Action? CopyRequested;

    public SetupPage()
    {
        Name = "SetupPage";
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        Width = PopupLayout.Width;
        Margin = Padding.Empty;
        BackColor = AppTheme.Surface;

        var header = Column(PopupLayout.Width, new Padding(20, 20, 20, 4));
        header.FlowDirection = FlowDirection.LeftToRight;
        _back = new PopupButton(PopupGlyph.Back)
        {
            Name = "SetupBack", AccessibleName = "Back to menu", BackColor = AppTheme.Surface
        };
        header.Controls.Add(_back);
        header.Controls.Add(new Label { Text = "Keypivo", AutoSize = true, Font = new Font("Segoe UI", 13, FontStyle.Bold), Margin = new Padding(10, 6, 0, 0) });
        Controls.Add(header);
        var content = Column(PopupLayout.Width, new Padding(24, 24, 24, 28));
        _symbol = new PictureBox { Size = new Size(40, 40), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding((PopupLayout.ContentWidth - 40) / 2, 0, 0, 22), TabStop = false };
        _title = TextLabel(PopupLayout.ContentWidth, new Font("Segoe UI", 16, FontStyle.Bold));
        _title.Name = "SetupTitle";
        _title.Margin = new Padding(0, 0, 0, 7);
        _subtitle = TextLabel(PopupLayout.ContentWidth);
        _subtitle.ForeColor = AppTheme.Muted;
        _subtitle.Margin = new Padding(0, 0, 0, 27);
        _commandLabel = TextLabel(PopupLayout.ContentWidth);
        _commandLabel.Margin = new Padding(0, 0, 0, 12);
        _commandBox = Column(PopupLayout.ContentWidth, new Padding(12));
        _commandBox.Name = "CommandBox";
        _commandBox.BackColor = AppTheme.Hover;
        _commandBox.Margin = new Padding(0, 0, 0, 12);
        _commandBox.Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, _commandBox.ClientRectangle, AppTheme.Border, ButtonBorderStyle.Solid);
        _command = TextLabel(PopupLayout.ContentWidth - 24, new Font("Consolas", 10.5f));
        _command.Name = "SetupCommand";
        _command.AccessibleName = "Command to run manually";
        _command.TextAlign = ContentAlignment.MiddleLeft;
        _commandBox.Controls.Add(_command);
        _copy = new PopupButton
        {
            Name = "CopyCommand", Text = "Copy command", AccessibleName = "Copy command"
        };
        _help = new LinkLabel
        {
            Name = "InstallationHelp", Text = "Other installation options", AccessibleName = "Other installation options (opens Microsoft Learn)",
            Width = PopupLayout.ContentWidth, Height = 30, TextAlign = ContentAlignment.MiddleCenter,
            LinkColor = AppTheme.Muted, ActiveLinkColor = AppTheme.Accent, VisitedLinkColor = AppTheme.Muted,
            LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0, 17, 0, 0)
        };
        _feedback = TextLabel(PopupLayout.ContentWidth);
        _feedback.Name = "SetupFeedback";
        _feedback.TextAlign = ContentAlignment.MiddleLeft;
        _feedback.ForeColor = AppTheme.Muted;
        _feedback.Margin = new Padding(0, 24, 0, 0);
        _feedback.Visible = false;
        _done = new PopupButton
        {
            Name = "SetupDone", Text = "Done", AccessibleName = "Done", Visible = false
        };
        content.Controls.AddRange([_symbol, _title, _subtitle, _commandLabel, _commandBox, _copy, _help, _feedback, _done]);
        Controls.Add(content);
        _back.Click += (_, _) => BackRequested?.Invoke();
        _copy.Click += (_, _) => CopyRequested?.Invoke();
        _done.Click += (_, _) => { if (_complete) BackRequested?.Invoke(); };
        _help.LinkClicked += (_, _) => OpenHelp();
    }

    public void SetInstructions(ManualSetup setup)
    {
        _setup = setup;
        _complete = false;
        _lastPresentation = null;
        SuspendLayout();
        _title.Text = setup.Title;
        _subtitle.Text = setup.Subtitle;
        _commandLabel.Text = setup.CommandLabel;
        _command.Text = setup.Command;
        _copy.Text = "Copy command";
        _commandLabel.Visible = _commandBox.Visible = _copy.Visible = true;
        _done.Visible = false;
        _help.Visible = setup.Kind == SetupKind.Install;
        _feedback.Text = "";
        _feedback.Visible = false;
        _symbol.Image?.Dispose();
        _symbol.Image = DrawSymbol(setup.Kind);
        ResumeLayout(true);
    }

    public bool Render(SetupCheckResult result, string? feedback, bool busy)
    {
        feedback = result.IsComplete ? _setup?.SuccessMessage : feedback ?? result.Message;
        var presentation = (result.IsComplete, result.CanCopy, feedback, busy);
        if (_lastPresentation == presentation) return false;
        _lastPresentation = presentation;
        var restoreFocus = ContainsFocus;
        SuspendLayout();
        if (_complete != result.IsComplete && _setup is not null)
        {
            _symbol.Image?.Dispose();
            _symbol.Image = DrawSymbol(_setup.Kind, result.IsComplete);
            _copy.Text = "Copy command";
        }
        _complete = result.IsComplete;
        _commandLabel.Visible = _commandBox.Visible = _copy.Visible = !_complete;
        _help.Visible = !_complete && _setup?.Kind == SetupKind.Install;
        _done.Visible = _complete;
        _copy.Enabled = !busy && result.CanCopy && !_complete;
        _back.Enabled = _done.Enabled = _help.Enabled = !busy;
        _feedback.Text = feedback ?? "";
        _feedback.Visible = !string.IsNullOrWhiteSpace(feedback);
        _feedback.ForeColor = _complete ? AppTheme.Accent : AppTheme.Muted;
        _feedback.TextAlign = _complete ? ContentAlignment.MiddleCenter : ContentAlignment.MiddleLeft;
        _feedback.Margin = _complete ? new Padding(0, 0, 0, (int)Math.Round(24 * DeviceDpi / 96d))
            : new Padding(0, (int)Math.Round(24 * DeviceDpi / 96d), 0, 0);
        ResumeLayout(true);
        // Focus the new action only when this page already owns keyboard focus.
        // Completion while working in PowerShell must not activate the popup.
        if (restoreFocus && !ContainsFocus) FocusPrimary();
        return true;
    }

    public void MarkCopied() => _copy.Text = "Copied";
    public void FocusPrimary() { if (!(_complete ? _done : _copy).Focus()) _back.Focus(); }

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
        TextAlign = ContentAlignment.MiddleCenter, Font = font ?? new Font("Segoe UI", 11),
        ForeColor = AppTheme.Text, Margin = Padding.Empty, UseMnemonic = false
    };

    private static Bitmap DrawSymbol(SetupKind kind, bool complete = false)
    {
        var bitmap = new Bitmap(80, 80);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new SolidBrush(AppTheme.AccentSurface);
        using var pen = new Pen(AppTheme.Accent, 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.FillEllipse(background, 0, 0, 79, 79);
        if (complete)
        {
            graphics.DrawLines(pen, [new(24, 40), new(35, 51), new(57, 29)]);
        }
        else if (kind == SetupKind.Install)
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
