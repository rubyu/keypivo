using System.Diagnostics;
using System.Runtime.InteropServices;
using Keypivo.Core;

namespace Keypivo.UiTests;

internal static class AutomaticSetupTests
{
    public static void Run()
    {
        foreach (var kind in Enum.GetValues<SetupKind>()) CheckFlow(kind);
        Console.WriteLine("PASS Automatic setup: timer-driven installation/sharing/unsharing, quiet instructions, background completion, read-error recovery, fresh copy guards, and Done without USB commands.");
    }

    private static void CheckFlow(SetupKind kind)
    {
        var client = new SetupClient
        {
            Missing = kind == SetupKind.Install,
            Key = kind == SetupKind.Install ? null : new UsbDevice
            {
                InstanceId = @"USB\VID_1050&PID_0407\automatic-setup", BusId = "4-4",
                PersistedGuid = kind == SetupKind.Unshare ? Guid.NewGuid() : null,
                IsForced = kind == SetupKind.Unshare,
                ClientIPAddress = kind == SetupKind.Unshare ? "172.22.0.2" : null
            }
        };
        var controller = new SwitchController(client, verification: new()
        {
            Timeout = TimeSpan.FromSeconds(2), PollInterval = TimeSpan.FromMilliseconds(10),
            StabilityPeriod = TimeSpan.FromMilliseconds(20)
        });
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json"));
        using var context = new TrayApplicationContext(controller, store,
            new(new() { Position = DisplayPosition.Center, Notifications = new() { Enabled = false } }, false), showWindow: true);
        PumpUntil(() => Application.OpenForms.OfType<StatusForm>().Any(f => f.Visible), "Initial menu");
        var popup = Application.OpenForms.OfType<StatusForm>().Single();
        Find<Button>(popup, "SwitchAction").PerformClick();
        PumpUntil(() => popup.IsSetupVisible && Find<Button>(popup, "CopyCommand").Enabled, "Setup instructions");
        var copy = Find<Button>(popup, "CopyCommand");
        var done = Find<Button>(popup, "SetupDone");
        var feedback = Find<Label>(popup, "SetupFeedback");
        var command = Find<Label>(popup, "SetupCommand");
        var pendingKey = client.Key;
        var expectedCommands = kind == SetupKind.Unshare ? 1 : 0;
        Assert(client.Commands == expectedCommands, "Only the initial forced-key detach may execute a USB command");
        Assert(!done.Visible && !feedback.Visible && command.Visible, "Pending setup must have instructions and Copy, with no waiting message");

        if (kind == SetupKind.Share)
        {
            // The UI still looks copyable, but the key has moved since its last poll.
            Clipboard.SetText("preserved before setup copy");
            client.Key = pendingKey! with { BusId = "6-1" };
            copy.PerformClick();
            PumpUntil(() => !copy.Enabled && feedback.Visible, "Stale command rejection");
            Assert(Clipboard.GetText() == "preserved before setup copy", "Copy must recheck the original key and preserve the clipboard on a stale target");
            client.Key = pendingKey;
            Finish(context.RefreshAsync());
            Assert(copy.Enabled && !feedback.Visible, "Reconnecting the original key must restore quiet instructions automatically");
        }

        using var terminal = new Form
        {
            Text = "External setup terminal", StartPosition = FormStartPosition.Manual,
            Location = Screen.FromControl(popup).WorkingArea.Location, Size = new Size(180, 110)
        };
        terminal.Show();
        terminal.Activate();
        Application.DoEvents();
        Assert(GetForegroundWindow() == terminal.Handle && !popup.TopMost, "The external terminal must own foreground focus");
        client.Missing = false;
        var completedKey = kind == SetupKind.Install ? null : pendingKey! with
        {
            PersistedGuid = kind == SetupKind.Share ? Guid.NewGuid() : null,
            IsForced = false, ClientIPAddress = null
        };
        client.Key = completedKey;
        // Do not explicitly refresh: exercise the application's actual polling timer.
        PumpUntil(() => done.Visible, "Automatic setup completion");
        var setup = new ManualSetup(kind, kind == SetupKind.Install ? null : pendingKey);
        Assert(popup.IsSetupVisible && !copy.Visible && !command.Visible && feedback.Text == setup.SuccessMessage,
            "Completion must show success and Done on the same page");
        Assert(GetForegroundWindow() == terminal.Handle && !popup.TopMost, "Background completion must not steal focus or pin the popup above PowerShell");
        Assert(client.Commands == expectedCommands, "Completion must not execute install, bind, unbind, or attach");
        Program.CheckBounds(popup);
        Program.CaptureScreen(popup, kind.ToString().ToLowerInvariant() + "-automatic-complete");

        client.FailRead = true;
        Finish(context.RefreshAsync());
        Assert(!done.Visible && copy.Visible && feedback.Visible && feedback.Text.Contains("Retrying automatically"),
            "A failed read must invalidate stale success and explain automatic recovery");
        client.FailRead = false;
        Finish(context.RefreshAsync());
        Assert(done.Visible && feedback.Text == setup.SuccessMessage, "A successful read must restore completion without a check button");

        if (kind == SetupKind.Share)
        {
            client.Key = pendingKey;
            Finish(context.RefreshAsync());
            Assert(!done.Visible && copy.Visible && !feedback.Visible, "Reverting sharing must restore the original command without a waiting message");
            client.Key = completedKey;
            Clipboard.SetText("preserved before completed setup");
            popup.ShowAt(DisplayPosition.Center);
            copy.PerformClick();
            PumpUntil(() => done.Visible && done.Enabled, "Copy-time completion");
            Assert(Clipboard.GetText() == "preserved before completed setup", "Copy must not overwrite the clipboard with a command that is already complete");
        }

        popup.ShowAt(DisplayPosition.Center);
        var reads = client.Reads;
        done.PerformClick();
        Assert(!popup.IsSetupVisible && client.Reads == reads && client.Commands == expectedCommands,
            "Done must only return to the menu, without querying or executing USB commands");
        context.ExitThread();
    }

    private static void Finish(Task task) { PumpUntil(() => task.IsCompleted, "Status read"); task.GetAwaiter().GetResult(); }
    private static void PumpUntil(Func<bool> complete, string operation)
    {
        var watch = Stopwatch.StartNew();
        do
        {
            Application.DoEvents();
            if (complete()) return;
            Thread.Sleep(5);
        } while (watch.Elapsed < TimeSpan.FromSeconds(6));
        throw new Exception($"Timed out: {operation}");
    }
    private static T Find<T>(Control parent, string name) where T : Control => parent.Controls.Find(name, true).OfType<T>().Single();
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    private sealed class SetupClient : IUsbipdClient
    {
        public bool Missing { get; set; }
        public bool FailRead { get; set; }
        public UsbDevice? Key { get; set; }
        public int Reads { get; private set; }
        public int Commands { get; private set; }
        public Task<IReadOnlyList<UsbDevice>> GetDevicesAsync(CancellationToken cancellationToken)
        {
            Reads++;
            if (FailRead) throw new UsbipdException("Status unavailable");
            if (Missing) throw new UsbipdNotFoundException();
            return Task.FromResult<IReadOnlyList<UsbDevice>>(Key is null ? [] : [Key with { }]);
        }
        public Task ExecuteAsync(UsbipdCommand command, string busId, CancellationToken cancellationToken)
        {
            Commands++;
            Assert(command == UsbipdCommand.Detach && Key is { IsForced: true }, "Only an explicit forced-key detach is expected in this test");
            Key = Key! with { ClientIPAddress = null };
            return Task.CompletedTask;
        }
    }
}
