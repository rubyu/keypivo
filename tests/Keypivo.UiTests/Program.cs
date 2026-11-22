using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Keypivo.Core;

namespace Keypivo.UiTests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // Keep the real Windows UI synchronization context installed between
        // nested DoEvents calls, including across disposable tray contexts.
        using var dispatcher = new Control();
        _ = dispatcher.Handle;
        var result = 1;
        dispatcher.BeginInvoke((Action)(() =>
        {
            result = RunChecks();
            Application.ExitThread();
        }));
        Application.Run();
        return result;
    }

    private static int RunChecks()
    {
        try
        {
            PopupPresentationTests.Run();
            PopupHeaderTests.Run();
            PollingPresentationTests.Run();
            StartupSettingsTests.Run();
            NotificationSettingsTests.Run();
            SettingsRenderingTests.Run();
            NotificationDispatchTests.Run();
            using var form = new StatusForm { AllowClose = true };
            var key = new UsbDevice { InstanceId = @"USB\VID_1050&PID_0407\preview", BusId = "4-4" };
            form.Render(new(ConnectionState.ToolMissing, null, 0, "Not installed"), false, false, null, SystemIcons.Application);
            form.ShowAt(DisplayPosition.Center);
            Application.DoEvents();
            var action = Find<Button>(form, "SwitchAction");
            Assert(action.Enabled && action.Text == "Set up…", "Missing tool must offer setup");
            Assert(!Find<Button>(form, "StatusDetails").Visible, "Missing tool must not expose an error-details row");
            form.SetupBackRequested += form.ShowMenu;
            foreach (var kind in Enum.GetValues<SetupKind>())
            {
                var setup = new ManualSetup(kind, kind == SetupKind.Install ? null : key);
                form.ShowSetup(setup);
                form.RenderSetup(new(false, true, null), null, false, false);
                Application.DoEvents();
                Assert(form.IsSetupVisible && !action.Visible, "Instructions replace the menu");
                var command = Find<Label>(form, "SetupCommand");
                Assert(command.Text == setup.Command && command.Visible, "The full command must be visible");
                Assert(Find<Button>(form, "CheckSetup").Text == setup.CheckLabel, "Instruction page must only check status");
                Assert(Find<LinkLabel>(form, "InstallationHelp").Visible == (kind == SetupKind.Install), "Installation help visibility");
                CheckBounds(form);
                Capture(form, kind.ToString().ToLowerInvariant());

                form.RenderSetup(new(false, false, "Reconnect the same security key, then check again."),
                    "Reconnect the same security key, then check again.", false, false);
                Application.DoEvents();
                Assert(!Find<Button>(form, "CopyCommand").Enabled, "A stale target must disable copy");
                Assert(Find<Label>(form, "SetupFeedback").Visible, "Pending confirmation must keep feedback on the page");
                Assert(form.IsSetupVisible, "Pending confirmation must keep instructions");
                CheckBounds(form);

                form.RenderSetup(new(true, true, null), null, false, false);
                Assert(form.IsSetupVisible, "Background polling must not navigate away");
                Find<Button>(form, "SetupBack").PerformClick();
                Application.DoEvents();
                Assert(!form.IsSetupVisible && action.Visible, "Returning must show the normal menu");
            }
            form.Render(DeviceSnapshot.FromDevices([key]), false, false, null, SystemIcons.Application);
            Assert(action.Text == "Set up for WSL…", "Unshared menu action");
            form.Render(DeviceSnapshot.FromDevices([key with { PersistedGuid = Guid.NewGuid() }]), false, false, null, SystemIcons.Application);
            Assert(action.Text == "Switch to WSL", "Shared menu action");
            Assert(Find<Label>(form, "ConnectionStatus").Text == "Available on Windows", "Windows status must identify its destination");
            Application.DoEvents();
            CheckBounds(form);
            Capture(form, "menu");
            CaptureScreen(form, "menu-screen");
            form.Render(DeviceSnapshot.FromDevices([key with { PersistedGuid = Guid.NewGuid(), ClientIPAddress = "172.22.0.2" }]), false, false, null, SystemIcons.Application);
            Assert(action.Text == "Switch to Windows", "Attached menu action");
            Assert(Find<Label>(form, "ConnectionStatus").Text == "Connected to WSL", "Attached status must identify WSL");
            Assert(StatePresentation.Title(ConnectionState.Attached) == "Connected to WSL", "Notifications and tray tooltip must identify WSL");
            Application.DoEvents();
            CheckBounds(form);
            Capture(form, "menu-wsl");
            using var settings = new SettingsForm(new(), true, true, _ => Task.CompletedTask);
            settings.Show();
            Application.DoEvents();
            var unshare = Find<LinkLabel>(settings, "UnshareKey");
            Assert(!unshare.Enabled, "An attached key must be returned to Windows before manual unsharing");
            settings.Close();
            form.Close();
            Console.WriteLine("PASS Native UI: setup routing, command presentation, copy guards, confirmation, menu actions, and control bounds.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    private static T Find<T>(Control parent, string name) where T : Control =>
        parent.Controls.Find(name, true).OfType<T>().Single();

    private static void Assert(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    internal static void CheckBounds(Control parent)
    {
        var bounds = parent is ScrollableControl { AutoScroll: true, VerticalScroll: { Visible: true } } scroll
            ? new Rectangle(0, scroll.DisplayRectangle.Top, parent.ClientSize.Width, scroll.DisplayRectangle.Height)
            : new Rectangle(Point.Empty, parent.ClientSize);
        foreach (Control child in parent.Controls)
        {
            if (!child.Visible) continue;
            Assert(child.Left >= bounds.Left && child.Top >= bounds.Top && child.Right <= bounds.Right + 1 && child.Bottom <= bounds.Bottom + 1,
                $"Control clipped: {child.Name} {child.Bounds} in {parent.Name} {parent.ClientSize}");
            if (child is Label label && label.AutoSize)
                Assert(label.Height >= label.GetPreferredSize(new Size(label.Width, 0)).Height, $"Label clipped: {label.Name}");
            CheckBounds(child);
        }
    }

    internal static void CaptureScreen(Form form, string name)
    {
        form.BringToFront();
        form.Activate();
        form.Refresh();
        Application.DoEvents();
        _ = DwmFlush();
        using var bitmap = new Bitmap(form.Width, form.Height);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(form.Location, Point.Empty, form.Size);
        var path = Path.GetFullPath(Path.Combine("artifacts", "ui", name + ".png"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bitmap.Save(path, ImageFormat.Png);
        Console.WriteLine($"UI_IMAGE {name} {Convert.ToBase64String(File.ReadAllBytes(path))}");
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    internal static void Capture(Form form, string name)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        var directory = Path.GetFullPath(Path.Combine("artifacts", "ui"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + ".png");
        bitmap.Save(path, ImageFormat.Png);
        // Small native renders can be reviewed through the job log as well as the artifact.
        Console.WriteLine($"UI_IMAGE {name} {Convert.ToBase64String(File.ReadAllBytes(path))}");
    }
}
