using System.Reflection;
using Keypivo.Core;

namespace Keypivo.UiTests;

internal static class PopupHeaderTests
{
    public static void Run()
    {
        using var popup = new StatusForm { AllowClose = true };
        var key = new UsbDevice { InstanceId = @"USB\VID_1050&PID_0407\header", BusId = "4-4", PersistedGuid = Guid.NewGuid() };
        var snapshot = DeviceSnapshot.FromDevices([key]);
        popup.Render(snapshot, false, false, null, SystemIcons.Application);
        popup.ShowAt(DisplayPosition.Center);
        Application.DoEvents();
        var tools = Find<FlowLayoutPanel>(popup, "HeaderActions");
        Assert(popup.Controls.Find("RefreshStatus", true).Length == 0, "The menu must not expose a refresh action");
        var info = Find<Button>(popup, "ToggleDeviceDetails");
        var settings = Find<Button>(popup, "OpenSettings");
        var exit = Find<Button>(popup, "ExitApplication");
        var action = Find<Button>(popup, "SwitchAction");
        var buttons = new[] { settings, exit };
        var headerButtons = new[] { info, settings, exit };
        Assert(tools.Controls.Cast<Control>().SequenceEqual(headerButtons), "Header order must be device details, Settings, Exit");
        Assert(headerButtons.All(button => button.Parent == tools && button.Text.Length == 0), "Utility actions must be icon buttons in the header");
        Assert(headerButtons.Select(button => button.AccessibilityObject.Name).SequenceEqual(new[] { "Show all device details", "Settings", "Exit Keypivo" }),
            "Icon-only buttons must expose meaningful accessible names");
        var tooltip = (ToolTip)typeof(StatusForm).GetField("_toolTip", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(popup)!;
        Assert(headerButtons.All(button => tooltip.GetToolTip(button) == button.AccessibleName), "Each icon must have a descriptive tooltip");
        var calls = new int[2];
        popup.SettingsRequested += () => calls[0]++;
        popup.ExitRequested += () => calls[1]++;
        foreach (var button in buttons) button.PerformClick();
        Assert(calls.SequenceEqual(new[] { 1, 1 }), "Each icon must invoke only its own action");

        popup.Render(snapshot, true, false, null, SystemIcons.Application);
        foreach (var button in buttons) button.PerformClick();
        Assert(buttons.All(button => !button.Enabled) && calls.SequenceEqual(new[] { 1, 1 }), "Switching must prevent header actions");
        popup.Render(snapshot, false, true, null, SystemIcons.Application);
        Assert(!settings.Enabled && exit.Enabled, "Foreground checks must keep Exit available while preventing overlapping work");
        popup.Render(snapshot, false, false, null, SystemIcons.Application);
        Assert(buttons.All(button => button.Enabled), "Completing work must restore header actions");

        info.Focus();
        Assert(popup.SelectNextControl(info, true, true, true, true) && settings.Focused, "Keyboard navigation must reach Settings after device details");
        Assert(popup.SelectNextControl(settings, true, true, true, true) && exit.Focused, "Keyboard navigation must reach Exit after Settings");
        Assert(popup.SelectNextControl(exit, true, true, true, true) && action.Focused, "Keyboard navigation must skip the empty heading and reach the primary action after the header");

        var defaultFont = popup.Font;
        foreach (var textScale in new[] { 1f, 1.5f, 2f })
        {
            using var font = new Font("Segoe UI", 12 * textScale);
            popup.Font = font;
            foreach (var current in new[] { snapshot, DeviceSnapshot.FromDevices([key with { ClientIPAddress = "172.22.0.2" }]), new DeviceSnapshot(ConnectionState.ToolMissing, null, 0, "Not installed") })
            {
                popup.Render(current, false, false, null, SystemIcons.Application);
                Application.DoEvents();
                Program.CheckBounds(popup);
                Assert(headerButtons.All(button => button.Size == info.Size && button.Top == info.Top),
                    "Header buttons must keep equal sizes and vertical alignment");
                Assert(headerButtons.Zip(headerButtons.Skip(1), (left, right) => right.Left - left.Right).Distinct().Count() == 1,
                    "All three header buttons must have equal gaps");
                var measured = TextRenderer.MeasureText(action.Text, action.Font);
                Assert(measured.Width <= action.Width - 24 && measured.Height <= action.Height,
                    "The primary action must remain readable with enlarged text");
            }
            Program.Capture(popup, $"header-text-{textScale * 100:0}");
            popup.Font = defaultFont;
        }
        CheckPaintCoverage();
        Console.WriteLine("PASS Popup header: equal spacing, accessible names/tooltips, action routing, busy guards, keyboard order, text scaling, and opaque repaint.");
    }

    private static void CheckPaintCoverage()
    {
        var paint = typeof(PopupButton).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var glyph in new PopupGlyph?[] { PopupGlyph.Back, PopupGlyph.Settings, PopupGlyph.Power, PopupGlyph.Info, null })
        {
            using var button = new PopupButton(glyph) { Text = glyph.HasValue ? "" : "Switch to Windows" };
            using var bitmap = new Bitmap(button.Width, button.Height);
            using var graphics = Graphics.FromImage(bitmap);
            foreach (var enabled in new[] { true, false, true })
            {
                button.Enabled = enabled;
                graphics.Clear(Color.Magenta);
                paint.Invoke(button, [new PaintEventArgs(graphics, button.ClientRectangle)]);
                for (var y = 0; y < bitmap.Height; y++)
                for (var x = 0; x < bitmap.Width; x++)
                    Assert(bitmap.GetPixel(x, y).ToArgb() != Color.Magenta.ToArgb(), "Every custom button must cover stale pixels on repaint");
            }
            graphics.Clear(Color.Magenta);
            var clip = new Rectangle(10, 10, 15, 15);
            graphics.SetClip(clip);
            paint.Invoke(button, [new PaintEventArgs(graphics, clip)]);
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (!clip.Contains(x, y)) Assert(bitmap.GetPixel(x, y).ToArgb() == Color.Magenta.ToArgb(), "Button painting must preserve partial repaint bounds");
        }
    }

    private static T Find<T>(Control parent, string name) where T : Control => parent.Controls.Find(name, true).OfType<T>().Single();
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
}
