using System.Runtime.InteropServices;

namespace Keypivo;

// One tooltip per popup, shared by the heading and all property values.
internal sealed class CopyFeedback : IDisposable
{
    private readonly ToolTip _toolTip = new() { UseAnimation = false, UseFading = false };
    private readonly Action<string> _writeText;
    private Control? _owner;
    private bool _disposed;

    internal CopyFeedback(Action<string>? writeText = null) => _writeText = writeText ?? Clipboard.SetText;

    internal void SetHint(Control owner, bool hasValue)
    {
        if (_disposed) return;
        // Associate the control before Show creates the native tooltip window.
        // This gives it the popup owner, so it stays above the topmost tray menu.
        _toolTip.SetToolTip(owner, hasValue ? "Click to copy" : null);
    }

    internal void Copy(Control owner, string value)
    {
        Hide();
        var message = "Copied";
        try { _writeText(value); }
        catch (ExternalException) { message = "Clipboard is busy. Try again."; }
        _owner = owner;
        var inset = (int)Math.Round(8 * owner.DeviceDpi / 96d);
        _toolTip.Show(message, owner, new Point(inset, owner.Height + inset), 1500);
    }

    internal void Hide(Control? owner = null)
    {
        if (_disposed || _owner is null || (owner is not null && owner != _owner)) return;
        _toolTip.Hide(_owner);
        _owner = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Hide();
        _toolTip.Dispose();
        _disposed = true;
    }
}

internal sealed class CopyableLabel : Label
{
    private readonly CopyFeedback _feedback;
    private bool HasValue => !string.IsNullOrWhiteSpace(Text) && Text != "-";

    internal CopyableLabel(CopyFeedback feedback)
    {
        _feedback = feedback;
        SetStyle(ControlStyles.Selectable, true);
        UseMnemonic = false;
        TabStop = false;
        AccessibleDescription = "Click or press Enter to copy the value.";
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        _feedback?.Hide(this);
        _feedback?.SetHint(this, HasValue);
        TabStop = HasValue;
        Cursor = HasValue ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        CopyValue();
    }

    internal void CopyValue()
    {
        if (Enabled && Visible && HasValue) _feedback.Copy(this, Text);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyData is Keys.Enter or Keys.Space)
        {
            CopyValue();
            e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible) _feedback?.Hide(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _feedback.Hide(this);
            _feedback.SetHint(this, false);
        }
        base.Dispose(disposing);
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new CopyAccessibleObject(this);

    private sealed class CopyAccessibleObject(CopyableLabel owner) : ControlAccessibleObject(owner)
    {
        public override string? Name
        {
            get => owner.AccessibleName is { Length: > 0 } caption ? $"{caption}: {owner.Text}" : owner.Text;
            set => owner.AccessibleName = value;
        }
        public override AccessibleRole Role => AccessibleRole.PushButton;
        public override string? DefaultAction => owner.HasValue ? "Copy" : null;
        public override void DoDefaultAction() => owner.CopyValue();
    }
}
