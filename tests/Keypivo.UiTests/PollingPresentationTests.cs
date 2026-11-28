using System.Diagnostics;
using Keypivo.Core;

namespace Keypivo.UiTests;

internal static class PollingPresentationTests
{
    public static void Run()
    {
        var client = new PollingClient();
        var controller = new SwitchController(client, verification: new()
        {
            Timeout = TimeSpan.FromSeconds(2),
            PollInterval = TimeSpan.FromMilliseconds(10),
            StabilityPeriod = TimeSpan.FromMilliseconds(20)
        });
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json"));
        using var context = new TrayApplicationContext(controller, store,
            new(new() { Position = DisplayPosition.Center }, false), showWindow: true);
        PumpUntil(() => Application.OpenForms.OfType<StatusForm>().Any(f => f.Visible), "Initial menu");
        var popup = Application.OpenForms.OfType<StatusForm>().Single();
        var action = Find<Button>(popup, "SwitchAction");
        var status = Find<Label>(popup, "ConnectionStatus");
        var enabledChanges = 0;
        var layouts = 0;
        foreach (var button in Descendants(popup).OfType<Button>()) button.EnabledChanged += (_, _) => enabledChanges++;
        popup.Layout += (_, _) => layouts++;

        var gate = client.BlockNextRead();
        var poll = context.RefreshAsync();
        Assert(!poll.IsCompleted && action.Enabled, "Background reads must not disable the menu");
        gate.SetResult();
        PumpUntil(() => poll.IsCompleted, "Background read");
        poll.GetAwaiter().GetResult();
        Assert(enabledChanges == 0 && layouts == 0, "Unchanged polling must not toggle controls or relayout the menu");

        client.Key = client.Key with { ClientIPAddress = "172.22.0.2" };
        Finish(context.RefreshAsync());
        Assert(status.Text == "Connected to WSL", "A changed snapshot must still update the menu");
        client.Key = client.Key with { ClientIPAddress = null };
        Finish(context.RefreshAsync());

        gate = client.BlockNextRead();
        poll = context.RefreshAsync();
        action.PerformClick();
        Assert(!action.Enabled && client.Commands == 0, "A user action must wait for the ongoing read");
        gate.SetResult();
        PumpUntil(() => action.Enabled && status.Text == "Connected to WSL", "Queued switch");
        Assert(client.Commands == 1, "The click during polling must execute exactly once");
        poll.GetAwaiter().GetResult();

        gate = client.BlockNextRead();
        poll = context.RefreshAsync();
        var reads = client.Reads;
        var joined = context.RefreshAsync();
        Assert(!joined.IsCompleted && action.Enabled && client.Reads == reads, "Repeated polling must join the current read without disabling controls");
        gate.SetResult();
        Finish(joined);
        Assert(action.Enabled && client.Reads == reads, "Joined polling must not start a duplicate read");

        client.Key = client.Key with { ClientIPAddress = null, PersistedGuid = null };
        Finish(context.RefreshAsync());
        action.PerformClick();
        PumpUntil(() => popup.IsSetupVisible && Find<Button>(popup, "CopyCommand").Enabled, "Sharing instructions");
        var setupLayouts = 0;
        Find<SetupPage>(popup, "SetupPage").Layout += (_, _) => setupLayouts++;
        enabledChanges = layouts = 0;
        gate = client.BlockNextRead();
        poll = context.RefreshAsync();
        gate.SetResult();
        Finish(poll);
        Assert(enabledChanges == 0 && layouts == 0 && setupLayouts == 0,
            "Unchanged polling must not toggle controls or relayout instructions");

        gate = client.BlockNextRead();
        poll = context.RefreshAsync();
        Find<Button>(popup, "SetupBack").PerformClick();
        Assert(!popup.IsSetupVisible, "Back must remain usable during automatic reads");
        gate.SetResult();
        Finish(poll);
        Assert(!popup.IsSetupVisible && action.Text == "Set up for WSL…", "A stale setup read must not reopen its page");

        // Exiting while a read and a user action are pending must cancel cleanly.
        gate = client.BlockNextRead();
        poll = context.RefreshAsync();
        action.PerformClick();
        context.ExitThread();
        Finish(poll);
        Assert(client.Commands == 1, "Exiting must cancel the queued action before a USB command is sent");
        Console.WriteLine("PASS Native polling: stable controls/layout, changed state, queued action, joined reads, setup Back, and exit.");
    }

    private static T Find<T>(Control parent, string name) where T : Control =>
        parent.Controls.Find(name, true).OfType<T>().Single();

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Finish(Task task)
    {
        PumpUntil(() => task.IsCompleted, "Async operation");
        task.GetAwaiter().GetResult();
    }

    private static void PumpUntil(Func<bool> complete, string operation)
    {
        var watch = Stopwatch.StartNew();
        do
        {
            Application.DoEvents();
            if (complete()) return;
            Thread.Sleep(5);
        } while (watch.Elapsed < TimeSpan.FromSeconds(5));
        throw new Exception($"Timed out: {operation}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class PollingClient : IUsbipdClient
    {
        private TaskCompletionSource? _nextRead;
        public UsbDevice Key { get; set; } = new()
        {
            InstanceId = @"USB\VID_1050&PID_0407\polling", BusId = "4-4", PersistedGuid = Guid.NewGuid()
        };
        public int Commands { get; private set; }
        public int Reads { get; private set; }

        public TaskCompletionSource BlockNextRead() => _nextRead = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<UsbDevice>> GetDevicesAsync(CancellationToken cancellationToken)
        {
            Reads++;
            var gate = _nextRead;
            _nextRead = null;
            if (gate is not null) await gate.Task.WaitAsync(cancellationToken);
            return [Key with { }]; // A fresh object with equal data must not trigger layout.
        }

        public Task ExecuteAsync(UsbipdCommand command, string busId, CancellationToken cancellationToken)
        {
            Commands++;
            Key = Key with { ClientIPAddress = command == UsbipdCommand.Attach ? "172.22.0.2" : null };
            return Task.CompletedTask;
        }
    }
}
