using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Keypivo.Core;

namespace Keypivo.UiTests;

internal static class CopyableValueTests
{
    public static void Run()
    {
        using var icons = new TrayIcons();
        using var popup = new StatusForm { AllowClose = true, KeepOpen = true };
        var device = new UsbDevice
        {
            InstanceId = @"USB\VID_1050&PID_0407\copy", BusId = "4-4",
            PersistedGuid = Guid.Parse("80a5370f-49a1-47a4-84ba-0543f78a860d"),
            Description = "USB 入力デバイス, Microsoft Usbccid Smartcard Reader (WUDF) & authentication"
        };
        var snapshot = DeviceSnapshot.FromDevices([device]);
        var icon = icons.For(snapshot.State);
        popup.Render(snapshot, false, false, null, icon);
        popup.ShowAt(DisplayPosition.Center);
        Application.DoEvents();
        var heading = Find<CopyableLabel>(popup, "DeviceHeading");
        var info = Find<Button>(popup, "ToggleDeviceDetails");
        var panel = Find<DeviceDetailsPanel>(popup, "DeviceDetails");
        var product = Find<CopyableLabel>(popup, "ProductValue");
        var guid = Find<CopyableLabel>(popup, "PersistedGUIDValue");
        var commands = 0;
        popup.SwitchRequested += () => commands++;
        popup.RefreshRequested += () => commands++;
        popup.SettingsRequested += () => commands++;
        popup.ExitRequested += () => commands++;

        foreach (var value in new[] { heading, product, guid }) CheckCopy(value);
        CheckCopy(heading);
        // Let the native tooltip finish painting before capturing its pixels.
        Thread.Sleep(250);
        Application.DoEvents();
        Assert(VisibleTooltips() > 0, "Copying must show a native tooltip");
        Program.CaptureScreen(popup, "device-value-copied");
        Assert(TooltipAbove(popup), "Copy feedback must be above the topmost tray popup");
        popup.Hide();
        Application.DoEvents();
        Assert(VisibleTooltips() == 0, "Hiding the popup must dismiss copy feedback");
        popup.ShowAt(DisplayPosition.Center);
        CheckCopy(heading);
        Thread.Sleep(1700);
        Application.DoEvents();
        Assert(VisibleTooltips() == 0, "Copy feedback must expire and remove its hover association");

        info.PerformClick();
        Application.DoEvents();
        foreach (var value in panel.Controls.OfType<CopyableLabel>()) CheckCopy(value);
        Assert(commands == 0, "Copying values and expanding details must not issue any device or application commands");

        // Exercise the keyboard and the action exposed to assistive technology.
        Assert(product.Focus(), "Property values must be keyboard focusable");
        Clipboard.SetText("previous value");
        SendKeys.SendWait("{ENTER}");
        Application.DoEvents();
        Assert(Clipboard.GetText() == product.Text, "Enter must copy the focused property");
        Clipboard.SetText("previous value");
        SendKeys.SendWait(" ");
        Application.DoEvents();
        Assert(Clipboard.GetText() == product.Text, "Space must copy the focused property");
        Assert(guid.AccessibilityObject.DefaultAction == "Copy", "Assistive technology must expose Copy");
        guid.AccessibilityObject.DoDefaultAction();
        Assert(Clipboard.GetText() == guid.Text, "The accessible action must copy the GUID");

        var longDescription = string.Join(" ", Enumerable.Repeat(device.Description, 30));
        popup.Render(DeviceSnapshot.FromDevices([device with { Description = longDescription, ClientIPAddress = "172.22.0.2" }]),
            false, false, null, icon);
        Application.DoEvents();
        CheckCopy(heading);
        Assert(Clipboard.GetText() == longDescription, "An ellipsized heading must copy the full Unicode description");
        var connection = Find<CopyableLabel>(popup, "ConnectionValue");
        panel.ScrollControlIntoView(connection);
        CheckCopy(connection);
        Assert(Clipboard.GetText() == "WSL", "Copy must use the current value after a status update");

        popup.Render(DeviceSnapshot.FromDevices([]), false, false, null, icon);
        Application.DoEvents();
        Assert(VisibleTooltips() == 0, "Updating the copied value must dismiss stale feedback");
        Clipboard.SetText("preserved");
        foreach (var value in panel.Controls.OfType<CopyableLabel>().Prepend(heading))
        {
            Click(value);
            Assert(value.Text == "-" && !value.TabStop && value.Cursor == Cursors.Default, "Missing values must not offer Copy");
        }
        Assert(Clipboard.GetText() == "preserved", "Missing values must leave the clipboard intact");
        popup.Hide();

        using var feedback = new CopyFeedback(_ => throw new ExternalException("Clipboard locked"));
        using var failureForm = new Form { Text = "Clipboard feedback", ClientSize = new Size(360, 130) };
        var failedValue = new CopyableLabel(feedback) { Text = "A value", AutoSize = true, Location = new Point(24, 24) };
        failureForm.Controls.Add(failedValue);
        failureForm.Show();
        failureForm.Activate();
        Application.DoEvents();
        Click(failedValue);
        Application.DoEvents();
        Assert(Clipboard.GetText() == "preserved" && VisibleTooltips() > 0, "A busy clipboard must show feedback without reporting success or losing the previous contents");
        Program.CaptureScreen(failureForm, "clipboard-busy");
        failureForm.Close();
        Assert(VisibleTooltips() == 0, "Disposing copied controls must dismiss their tooltip");
        Console.WriteLine("PASS Copyable values: heading and all fields, Unicode and unabridged text, live updates, keyboard/accessibility, placeholders, tooltip cleanup, and clipboard failure.");
    }

    private static void CheckCopy(CopyableLabel value)
    {
        Clipboard.SetText("previous value");
        Click(value);
        Application.DoEvents();
        Assert(value.TabStop && value.Cursor == Cursors.Hand && Clipboard.GetText() == value.Text,
            $"Clicking {value.Name} must copy its complete displayed value");
    }

    private static void Click(Control control) => typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, [EventArgs.Empty]);

    private static int VisibleTooltips()
    {
        var count = 0;
        EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
        {
            var name = new StringBuilder(256);
            GetClassName(window, name, name.Capacity);
            // WinForms registers a versioned wrapper around the native class.
            var className = name.ToString();
            if ((className == "tooltips_class32" || className.Contains(".tooltips_class32.", StringComparison.Ordinal))
                && IsWindowVisible(window)) count++;
            return true;
        }, IntPtr.Zero);
        return count;
    }

    private static bool TooltipAbove(Form popup)
    {
        for (var window = GetWindow(popup.Handle, 3); window != IntPtr.Zero; window = GetWindow(window, 3))
        {
            var name = new StringBuilder(256);
            GetClassName(window, name, name.Capacity);
            if (name.ToString().Contains("tooltips_class32", StringComparison.Ordinal) && IsWindowVisible(window)) return true;
        }
        return false;
    }

    private static T Find<T>(Control parent, string name) where T : Control => parent.Controls.Find(name, true).OfType<T>().Single();
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint threadId, EnumWindowProc callback, IntPtr parameter);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
}
