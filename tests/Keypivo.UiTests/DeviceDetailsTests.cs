using Keypivo.Core;

namespace Keypivo.UiTests;

internal static class DeviceDetailsTests
{
    public static void Run()
    {
        using var icons = new TrayIcons();
        using var popup = new StatusForm { AllowClose = true, KeepOpen = true };
        var device = new UsbDevice
        {
            InstanceId = @"USB\VID_1050&PID_0407\details", BusId = "4-4",
            PersistedGuid = Guid.Parse("6f9619ff-8b86-4b80-b42d-00c04fc964ff"),
            Description = "USB Input Device, Microsoft Usbccid Smartcard Reader (WUDF)"
        };
        var snapshot = DeviceSnapshot.FromDevices([device]);
        var icon = icons.For(snapshot.State);
        popup.Render(snapshot, false, false, null, icon);
        popup.ShowAt(DisplayPosition.Center);
        Application.DoEvents();
        var info = Find<PopupButton>(popup, "ToggleDeviceDetails");
        var panel = Find<DeviceDetailsPanel>(popup, "DeviceDetails");
        var product = Find<Label>(popup, "ProductValue");
        var guid = Find<Label>(popup, "PersistedGUIDValue");
        Assert(!panel.Expanded && !info.Selected, "Device details must initially be compact");
        Assert(VisibleValues(panel).SequenceEqual(new[] { "ProductValue", "PersistedGUIDValue" }), "Compact mode must show only Product and Persisted GUID");
        CheckRows(panel);
        Assert(product.Left == guid.Left && product.Width == guid.Width, "Product and GUID must share a value column");
        var guidCaption = Find<Label>(popup, "PersistedGUIDCaption");
        Assert(TextRenderer.MeasureText(guidCaption.Text, guidCaption.Font).Width <= guidCaption.Width,
            "The Persisted GUID caption must fit on one line at the default text size");
        var valueLeft = product.Left;
        Program.CaptureScreen(popup, "details-compact");

        var actions = 0;
        popup.SwitchRequested += () => actions++;
        popup.RefreshRequested += () => actions++;
        info.PerformClick();
        Application.DoEvents();
        Assert(panel.Expanded && info.Selected && actions == 0, "Info must only expand details without issuing a device action");
        Assert(info.AccessibleName == "Show compact device details", "Info must describe the next action for assistive technology");
        Assert(VisibleValues(panel).Count() == 7, "Expanded mode must show all seven fields");
        Assert(product.Left == valueLeft && guid.Left == valueLeft, "Expanding must retain the same value column");
        var separator = Find<Panel>(popup, "VendorSeparator");
        Assert(separator.Top >= Find<Label>(popup, "VendorValue").Bottom && separator.Bottom < Find<Label>(popup, "DescriptionValue").Top,
            "The divider must sit after Vendor, before Description");
        Assert(Find<Label>(popup, "ConnectionValue").Text == "Windows" && Find<Label>(popup, "SharingValue").Text == "Shared", "Connection and sharing must remain distinct");
        CheckRows(panel);
        Program.CheckBounds(popup);
        Program.CaptureScreen(popup, "details-expanded");

        var layouts = 0;
        panel.Layout += (_, _) => layouts++;
        popup.Render(snapshot with { Device = device with { } }, false, false, null, icon);
        Application.DoEvents();
        Assert(layouts == 0, "Equivalent polls must not relayout device details");
        popup.Render(DeviceSnapshot.FromDevices([device with { ClientIPAddress = "172.22.0.2" }]), false, false, null, icons.For(ConnectionState.Attached));
        Assert(Find<Label>(popup, "ConnectionValue").Text == "WSL" && panel.Expanded, "State changes must update details without collapsing them");
        Program.Capture(popup, "details-wsl");
        popup.Render(DeviceSnapshot.FromDevices([]), false, false, null, icons.For(ConnectionState.NotConnected));
        Assert(panel.Controls.OfType<Label>().Where(label => label.Name.EndsWith("Value", StringComparison.Ordinal)).All(label => label.Text == "—"),
            "Removing a device must clear every previous value");
        info.PerformClick();
        Assert(!panel.Expanded && info.AccessibleName == "Show all device details", "Info must return to compact mode");

        var defaultFont = popup.Font;
        foreach (var scale in new[] { 1f, 1.5f, 2f })
        {
            using var font = new Font("Segoe UI", 12 * scale);
            popup.Font = font;
            popup.Render(snapshot, false, false, null, icon);
            Application.DoEvents();
            CheckRows(panel);
            Program.CheckBounds(popup);
            info.PerformClick();
            Application.DoEvents();
            CheckRows(panel);
            Program.CheckBounds(popup);
            Assert(popup.Height <= Screen.FromControl(popup).WorkingArea.Height, "Expanded details must fit the working area");
            Assert(!panel.HorizontalScroll.Visible, "Enlarged text must wrap without horizontal scrolling");
            Program.Capture(popup, $"details-text-{scale * 100:0}");
            info.PerformClick();
            popup.Font = defaultFont;
        }

        var longDescription = string.Join(" ", Enumerable.Repeat("USB Input Device & Smartcard Reader", 45));
        popup.Render(DeviceSnapshot.FromDevices([device with { Description = longDescription }]), false, false, null, icon);
        info.PerformClick();
        Application.DoEvents();
        Assert(panel.VerticalScroll.Visible && !panel.HorizontalScroll.Visible, "Long descriptions must scroll within the detail area");
        CheckRows(panel);
        panel.ScrollControlIntoView(guid);
        Application.DoEvents();
        Assert(guid.Bottom <= panel.ClientSize.Height && guid.Top >= 0, "Scrolling must make the full GUID reachable");
        Assert(popup.Height <= Screen.FromControl(popup).WorkingArea.Height, "Long details must not push the popup outside the screen");
        info.PerformClick();
        Application.DoEvents();
        Assert(panel.AutoScrollPosition == Point.Empty && !panel.VerticalScroll.Visible, "Collapsing must reset scrolling");
        CheckRows(panel);
        Console.WriteLine("PASS Device details: compact/full toggle, aligned values, Vendor divider, live states, clearing, stable polling, enlarged text, and scrollable long descriptions.");
    }

    private static IEnumerable<string> VisibleValues(Control panel) => panel.Controls.OfType<Label>()
        .Where(label => label.Visible && label.Name.EndsWith("Value", StringComparison.Ordinal)).Select(label => label.Name);

    private static void CheckRows(Control panel)
    {
        var values = panel.Controls.OfType<Label>().Where(label => label.Visible && label.Name.EndsWith("Value", StringComparison.Ordinal)).ToArray();
        Assert(values.Select(label => label.Left).Distinct().Count() == 1, "Every value must begin in the same column");
        foreach (var label in panel.Controls.OfType<Label>().Where(label => label.Visible))
        {
            Assert(!label.UseMnemonic, "Device names and descriptions must preserve literal ampersands");
            Assert(label.Height >= label.GetPreferredSize(new Size(label.Width, 0)).Height, $"Text clipped: {label.Name}");
        }
        foreach (var pair in values.Zip(values.Skip(1)))
            Assert(pair.First.Bottom < pair.Second.Top, "Detail rows must not overlap");
    }

    private static T Find<T>(Control parent, string name) where T : Control => parent.Controls.Find(name, true).OfType<T>().Single();
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
}
