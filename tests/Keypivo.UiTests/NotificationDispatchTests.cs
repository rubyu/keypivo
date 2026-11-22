using System.Diagnostics;
using Keypivo.Core;

namespace Keypivo.UiTests;

internal static class NotificationDispatchTests
{
    public static void Run()
    {
        using (var fixture = new Fixture(new() { ConnectedToWsl = false }, ConnectionState.Attached))
            Assert(fixture.Messages.Single().Contains("Connected to WSL"), "Launch notification is independent of later WSL notifications");
        using (var fixture = new Fixture(new() { CurrentStatusAtLaunch = false }, ConnectionState.Attached))
        {
            Assert(fixture.Messages.Count == 0, "Launch toggle must suppress the initial status");
            fixture.Client.SetState(ConnectionState.WindowsShared);
            fixture.Refresh();
            Assert(fixture.Messages.Single().Contains("Available on Windows"), "Launch toggle must not suppress later changes");
        }
        foreach (var enabled in new[] { false, true })
        {
            using var fixture = new Fixture(new() { SettingsCouldNotBeLoaded = enabled }, recovered: true);
            Assert(fixture.Messages.Count == (enabled ? 1 : 0), "Settings recovery must use its own preference without a second launch message");
        }
        using (var fixture = new Fixture(new() { Enabled = false }, recovered: true))
            Assert(fixture.Messages.Count == 0, "Master must mute settings recovery");

        using (var fixture = new Fixture(new() { CurrentStatusAtLaunch = false }))
        {
            foreach (var state in Enum.GetValues<ConnectionState>().Where(state => NotificationSettings.ForState(state) is not null))
            {
                var kind = NotificationSettings.ForState(state)!.Value;
                var baseline = state == ConnectionState.NotConnected ? ConnectionState.WindowsShared : ConnectionState.NotConnected;
                fixture.Client.SetState(baseline);
                fixture.Refresh();
                fixture.Apply(new NotificationSettings().WithSelection(kind, false));
                fixture.Messages.Clear();
                fixture.Client.SetState(state);
                fixture.Refresh();
                Assert(fixture.Messages.Count == 0, $"Disabled {kind} must not notify");
                Assert(fixture.Status.Text == StatePresentation.Title(state), "Muted notifications must not prevent status updates");
                fixture.Apply(new());
                fixture.Refresh();
                Assert(fixture.Messages.Count == 0, "Re-enabling must not replay suppressed events");
                fixture.Client.SetState(baseline);
                fixture.Refresh();
                fixture.Messages.Clear();
                fixture.Client.SetState(state);
                fixture.Refresh();
                Assert(fixture.Messages.Count == 1 && fixture.Messages[0].Contains(StatePresentation.Title(state)), "Enabled state must notify once");
                fixture.Refresh();
                Assert(fixture.Messages.Count == 1, "Unchanged polling must not repeat a notification");
            }
            fixture.Apply(new() { Enabled = false });
            fixture.Messages.Clear();
            fixture.Client.SetState(ConnectionState.Attached);
            fixture.Refresh();
            Assert(fixture.Messages.Count == 0 && fixture.Status.Text == "Connected to WSL", "Master must mute messages without hiding state");
        }

        using (var fixture = new Fixture(new() { CurrentStatusAtLaunch = false, ActionFailed = false }))
        {
            fixture.Client.FailCommand = true;
            fixture.Switch();
            Assert(fixture.Messages.Count == 0 && Find<Button>(fixture.Popup, "StatusDetails").Visible,
                "Muted command failures must remain visible in the app");
            fixture.Apply(new());
            fixture.Switch();
            Assert(fixture.Messages.Count == 1 && fixture.Messages[0].Contains("could not be completed"), "Operation failure preference must be applied");
        }
        foreach (var enabled in new[] { false, true })
        {
            using var fixture = new Fixture(new() { CurrentStatusAtLaunch = false, ChangeNotConfirmed = enabled, ConnectedToWsl = enabled });
            fixture.Client.DelayChange = true;
            fixture.Switch();
            Assert(fixture.Controller.PendingVerification is not null, "Fake switch must exercise a confirmation timeout");
            Assert(fixture.Messages.Count == (enabled ? 1 : 0), "Timeout must follow its own setting");
            fixture.Messages.Clear();
            fixture.Client.SetState(ConnectionState.Attached);
            fixture.Refresh();
            Assert(fixture.Controller.PendingVerification is null, "A later observation must recover the operation");
            Assert(fixture.Messages.Count == (enabled ? 1 : 0), "Late recovery must use the destination preference exactly once");
            Assert(fixture.Status.Text == "Connected to WSL", "Muted recovery must still update status");
        }
        using (var fixture = new Fixture(new() { CurrentStatusAtLaunch = false }))
        {
            fixture.Switch();
            Assert(fixture.Messages.Count == 1 && fixture.Messages[0].Contains("Connected to WSL"), "Successful attach must use the destination notification");
            fixture.Apply(new() { AvailableOnWindows = false });
            fixture.Messages.Clear();
            fixture.Switch();
            Assert(fixture.Messages.Count == 0 && fixture.Status.Text == "Available on Windows", "Successful detach must obey the Windows preference");
        }
        Console.WriteLine("PASS Notification delivery: launch, all states, recovery, failures, timeout, late confirmation, deduplication, live Save, master mute.");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "keypivo-delivery-" + Guid.NewGuid().ToString("N") + ".json");
        public Client Client { get; } = new();
        public SwitchController Controller { get; }
        public TrayApplicationContext Context { get; }
        public List<string> Messages { get; } = [];
        public StatusForm Popup { get; }
        public Label Status => Find<Label>(Popup, "ConnectionStatus");

        public Fixture(NotificationSettings settings, ConnectionState initial = ConnectionState.WindowsShared, bool recovered = false)
        {
            Client.SetState(initial);
            Controller = new(Client, verification: new()
            {
                Timeout = TimeSpan.FromMilliseconds(100), PollInterval = TimeSpan.FromMilliseconds(5), StabilityPeriod = TimeSpan.Zero
            });
            Context = new(Controller, new(_path), new(new() { Position = DisplayPosition.Center, Notifications = settings }, recovered),
                showWindow: true, showBalloon: (_, message, _) => Messages.Add(message));
            PumpUntil(() => Application.OpenForms.OfType<StatusForm>().Any(f => f.Visible));
            Popup = Application.OpenForms.OfType<StatusForm>().Single();
        }

        public void Refresh() => Finish(Context.RefreshAsync());
        public void Apply(NotificationSettings settings) => Finish(Context.ApplySettingsAsync(new() { Position = DisplayPosition.Center, Notifications = settings }));
        public void Switch()
        {
            Assert(SynchronizationContext.Current is WindowsFormsSynchronizationContext, "Switch continuations must run on the UI thread");
            var button = Find<Button>(Popup, "SwitchAction");
            button.PerformClick();
            PumpUntil(() => button.Enabled);
        }
        public void Dispose()
        {
            Context.ExitThread();
            Context.Dispose();
            File.Delete(_path);
        }
    }

    private sealed class Client : IUsbipdClient
    {
        private readonly UsbDevice _baseKey = new() { InstanceId = @"USB\VID_1050&PID_0407\notifications", BusId = "4-4", PersistedGuid = Guid.NewGuid() };
        private UsbDevice? _key;
        private Exception? _error;
        public bool FailCommand { get; set; }
        public bool DelayChange { get; set; }
        public void SetState(ConnectionState state)
        {
            _error = state switch { ConnectionState.Error => new IOException("Read failed"), ConnectionState.ToolMissing => new UsbipdNotFoundException(), _ => null };
            _key = state switch
            {
                ConnectionState.NotConnected or ConnectionState.Error or ConnectionState.ToolMissing => null,
                ConnectionState.Attached => _baseKey with { ClientIPAddress = "172.22.0.2" },
                ConnectionState.WindowsUnshared => _baseKey with { PersistedGuid = null },
                ConnectionState.ForcedShared => _baseKey with { IsForced = true },
                _ => _baseKey
            };
        }
        public Task<IReadOnlyList<UsbDevice>> GetDevicesAsync(CancellationToken cancellationToken)
        {
            if (_error is not null) throw _error;
            return Task.FromResult<IReadOnlyList<UsbDevice>>(_key is null ? [] : [_key]);
        }
        public Task ExecuteAsync(UsbipdCommand command, string busId, CancellationToken cancellationToken)
        {
            if (FailCommand) throw new UsbipdException("Command failed");
            if (!DelayChange) SetState(command == UsbipdCommand.Attach ? ConnectionState.Attached : ConnectionState.WindowsShared);
            return Task.CompletedTask;
        }
    }

    private static void Finish(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void PumpUntil(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        do { Application.DoEvents(); if (done()) return; Thread.Sleep(5); } while (watch.Elapsed < TimeSpan.FromSeconds(5));
        throw new Exception("Timed out waiting for notification test");
    }
    private static T Find<T>(Control parent, string name) where T : Control => parent.Controls.Find(name, true).OfType<T>().Single();
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
