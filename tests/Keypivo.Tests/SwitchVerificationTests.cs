using System.Diagnostics;
using Keypivo.Core;

internal static class SwitchVerificationTests
{
    internal static SwitchVerificationOptions FastOptions { get; } = new()
    {
        Timeout = TimeSpan.FromSeconds(1),
        PollInterval = TimeSpan.FromMilliseconds(10),
        StabilityPeriod = TimeSpan.FromMilliseconds(30)
    };

    private static SwitchVerificationOptions TimeoutOptions => FastOptions with { Timeout = TimeSpan.FromMilliseconds(150) };

    public static IEnumerable<(string Name, Func<Task> Run)> All =>
    [
        ("Verification: detach tolerates missing and disconnected records without publishing them", async () =>
        {
            var fake = new FakeClient(Key(attached: true)) { ApplyChanges = false };
            var observations = 0;
            fake.OnRead = () =>
            {
                if (fake.Commands.Count == 0) return;
                fake.Devices = ++observations switch
                {
                    1 or 2 => [],
                    3 => [Key() with { BusId = null }],
                    _ => [Key()]
                };
            };
            var controller = Controller(fake);
            var published = new List<ConnectionState>();
            controller.SnapshotChanged += snapshot => published.Add(snapshot.State);
            await controller.ExecuteAsync(SwitchAction.Detach);
            Equal(ConnectionState.WindowsShared, controller.Current.State);
            Equal(false, published.Contains(ConnectionState.NotConnected));
            Equal(1, published.Count(state => state == ConnectionState.WindowsShared));
            Commands(fake, UsbipdCommand.Detach);
        }),
        ("Verification: a brief expected state followed by removal resets the stability window", async () =>
        {
            var options = FastOptions with { StabilityPeriod = TimeSpan.FromMilliseconds(150), Timeout = TimeSpan.FromSeconds(2) };
            var fake = new FakeClient(Key(attached: true)) { ApplyChanges = false };
            var observations = 0;
            var restored = new Stopwatch();
            fake.OnRead = () =>
            {
                if (fake.Commands.Count == 0) return;
                observations++;
                fake.Devices = observations == 2 ? [] : [Key()];
                if (observations == 3) restored.Start();
            };
            var controller = new SwitchController(fake, verification: options);
            await controller.ExecuteAsync(SwitchAction.Detach);
            Equal(true, restored.IsRunning);
            Equal(true, restored.Elapsed >= options.StabilityPeriod);
            Equal(ConnectionState.WindowsShared, controller.Current.State);
        }),
        ("Verification: bind and attach follow the original key when enumeration order changes", async () =>
        {
            var fake = new FakeClient(Key(shared: false), Other()) { ApplyChanges = false };
            var observations = 0;
            fake.OnRead = () =>
            {
                if (fake.Commands.Count == 0) return;
                fake.Devices = fake.Commands.Count == 1 && ++observations < 3
                    ? [Other(), Key() with { BusId = null }]
                    : [Other(), Key(attached: fake.Commands.Count == 2) with { InstanceId = Key().InstanceId.ToLowerInvariant() }];
            };
            var controller = Controller(fake);
            await controller.ExecuteAsync(SwitchAction.Attach);
            Commands(fake, UsbipdCommand.Bind, UsbipdCommand.Attach);
            Equal(true, fake.Commands.All(command => command.BusId == "4-4"));
            Equal(ConnectionState.Attached, controller.Current.State);
            Equal("4-4", controller.Current.Device?.BusId);
        }),
        ("Verification: unbind also tolerates USB re-enumeration", async () =>
        {
            var fake = new FakeClient(Key(attached: true)) { ApplyChanges = false };
            var observations = 0;
            fake.OnRead = () =>
            {
                if (fake.Commands.Count == 1) fake.Devices = [Key()];
                if (fake.Commands.Count == 2)
                    fake.Devices = ++observations < 3 ? [] : [Key(shared: false)];
            };
            var controller = Controller(fake);
            await controller.ExecuteAsync(SwitchAction.Unshare);
            Commands(fake, UsbipdCommand.Detach, UsbipdCommand.Unbind);
            Equal(ConnectionState.WindowsUnshared, controller.Current.State);
        }),
        ("Verification: a replacement at the same bus cannot authorize unbind", async () =>
        {
            var fake = new FakeClient(Key(attached: true)) { ApplyChanges = false };
            fake.OnCommand = (_, _, _) => { fake.Devices = [Other() with { BusId = "4-4" }]; return Task.CompletedTask; };
            var controller = new SwitchController(fake, verification: TimeoutOptions);
            var timeout = await Throws<SwitchVerificationTimeoutException>(() => controller.ExecuteAsync(SwitchAction.Unshare));
            Equal(SwitchAction.Unshare, timeout.Action);
            Equal(ConnectionState.NotConnected, controller.Current.State);
            Commands(fake, UsbipdCommand.Detach);
        }),
        ("Recovery: timeout clears only after the original key reaches a stable expected state", async () =>
        {
            var (fake, controller, timeout) = await TimedOutDetach();
            var recovered = new List<(SwitchVerificationTimeoutException Timeout, DeviceSnapshot Snapshot)>();
            controller.VerificationRecovered += (warning, snapshot) => recovered.Add((warning, snapshot));
            // A different key and even the same instance at a different bus cannot confirm this operation.
            foreach (var candidate in new[] { Other(), Key() with { BusId = "6-1" } })
            {
                fake.Devices = [candidate];
                await ObserveStableAsync(controller);
                Equal(0, recovered.Count);
                Equal(timeout, controller.PendingVerification);
                Equal(false, controller.IsConfirmingCurrentState);
            }
            fake.Devices = [Other(attached: true), Key()];
            await controller.RefreshAsync();
            Equal(0, recovered.Count);
            await Task.Delay(FastOptions.StabilityPeriod * 2);
            await controller.RefreshAsync();
            Equal(1, recovered.Count);
            Equal(timeout, recovered[0].Timeout);
            Equal("4-4", recovered[0].Snapshot.Device?.BusId);
            Equal(ConnectionState.WindowsShared, recovered[0].Snapshot.State);
            Equal<SwitchVerificationTimeoutException?>(null, controller.PendingVerification);
            await controller.RefreshAsync();
            Equal(1, recovered.Count);
            Commands(fake, UsbipdCommand.Detach);
        }),
        ("Recovery: another disappearance restarts confirmation", async () =>
        {
            var (fake, controller, _) = await TimedOutDetach();
            var recovered = 0;
            controller.VerificationRecovered += (_, _) => recovered++;
            fake.Devices = [Key()];
            await controller.RefreshAsync();
            Equal(true, controller.IsConfirmingCurrentState);
            await Task.Delay(FastOptions.StabilityPeriod * 2);
            fake.Devices = [];
            await controller.RefreshAsync();
            Equal(false, controller.IsConfirmingCurrentState);
            fake.Devices = [Key()];
            await controller.RefreshAsync();
            Equal(0, recovered);
            await Task.Delay(FastOptions.StabilityPeriod * 2);
            await controller.RefreshAsync();
            Equal(1, recovered);
            Equal(false, controller.IsConfirmingCurrentState);
        }),
        ("Recovery: an actual command failure never becomes a recoverable timeout", async () =>
        {
            var fake = new FakeClient(Key(attached: true))
            {
                OnCommand = (_, _, _) => throw new UsbipdException("The detach command failed.")
            };
            var controller = Controller(fake);
            var recovered = 0;
            controller.VerificationRecovered += (_, _) => recovered++;
            var failure = await Throws<UsbipdException>(() => controller.ExecuteAsync(SwitchAction.Detach));
            Equal(false, failure is SwitchVerificationTimeoutException);
            fake.Devices = [Key()];
            await ObserveStableAsync(controller);
            Equal(0, recovered);
            Equal<SwitchVerificationTimeoutException?>(null, controller.PendingVerification);
            Commands(fake, UsbipdCommand.Detach);
        }),
        ("Recovery: a delayed bind alone cannot claim attachment or run another command", async () =>
        {
            var fake = new FakeClient(Key(shared: false)) { ApplyChanges = false };
            fake.OnCommand = (_, _, _) => { fake.Devices = []; return Task.CompletedTask; };
            var controller = new SwitchController(fake, verification: TimeoutOptions);
            var timeout = await Throws<SwitchVerificationTimeoutException>(() => controller.ExecuteAsync(SwitchAction.Attach));
            var recovered = 0;
            controller.VerificationRecovered += (_, _) => recovered++;
            fake.Devices = [Key()];
            await ObserveStableAsync(controller);
            Equal(0, recovered);
            Equal(timeout, controller.PendingVerification);
            Commands(fake, UsbipdCommand.Bind);
        }),
        ("Verification: the deadline cancels a slow state query and releases the lock", async () =>
        {
            var fake = new FakeClient(Key(attached: true));
            fake.OnReadAsync = token => fake.Commands.Count > 0 ? Task.Delay(Timeout.InfiniteTimeSpan, token) : Task.CompletedTask;
            var controller = new SwitchController(fake, verification: TimeoutOptions);
            await Throws<SwitchVerificationTimeoutException>(() => controller.ExecuteAsync(SwitchAction.Detach).WaitAsync(TimeSpan.FromSeconds(3)));
            fake.OnReadAsync = null;
            await controller.RefreshAsync();
            Equal(ConnectionState.WindowsShared, controller.Current.State);
            Commands(fake, UsbipdCommand.Detach);
        }),
        ("Verification: cancellation during re-enumeration is not a timeout", async () =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var fake = new FakeClient(Key(attached: true));
            fake.OnReadAsync = async token =>
            {
                if (fake.Commands.Count == 0) return;
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            };
            var controller = Controller(fake);
            using var cancel = new CancellationTokenSource();
            var operation = controller.ExecuteAsync(SwitchAction.Detach, cancel.Token);
            await entered.Task;
            cancel.Cancel();
            await Throws<OperationCanceledException>(() => operation);
            Equal<SwitchVerificationTimeoutException?>(null, controller.PendingVerification);
            fake.OnReadAsync = null;
            await controller.RefreshAsync();
            Equal(ConnectionState.WindowsShared, controller.Current.State);
        }),
        ("Verification: polling, duplicate operations, and filter edits cannot interrupt confirmation", async () =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var fake = new FakeClient(Key(attached: true));
            fake.OnReadAsync = async token =>
            {
                if (fake.Commands.Count == 0) return;
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            };
            var controller = Controller(fake);
            var operation = controller.ExecuteAsync(SwitchAction.Detach);
            await entered.Task;
            try
            {
                var reads = fake.Reads;
                await controller.RefreshAsync();
                Equal(reads, fake.Reads);
                await Throws<UsbipdException>(() => controller.ExecuteAsync(SwitchAction.Toggle));
                await Throws<UsbipdException>(() => controller.UpdateFilterAsync(new("1051", "")));
            }
            finally { release.TrySetResult(); }
            await operation;
            Commands(fake, UsbipdCommand.Detach);
        }),
        ("Recovery: changing the filter abandons the previous confirmation", async () =>
        {
            var (fake, controller, _) = await TimedOutDetach();
            var recovered = 0;
            controller.VerificationRecovered += (_, _) => recovered++;
            await controller.UpdateFilterAsync(new("1051", ""));
            Equal<SwitchVerificationTimeoutException?>(null, controller.PendingVerification);
            fake.Devices = [Key()];
            await ObserveStableAsync(controller);
            Equal(0, recovered);
            Equal(ConnectionState.NotConnected, controller.Current.State);
        }),
        ("Recovery: a new user action supersedes the old timeout", async () =>
        {
            var (fake, controller, _) = await TimedOutDetach();
            var recovered = 0;
            controller.VerificationRecovered += (_, _) => recovered++;
            fake.Devices = [Key(attached: true)];
            await controller.ExecuteAsync(SwitchAction.Attach);
            Equal<SwitchVerificationTimeoutException?>(null, controller.PendingVerification);
            fake.Devices = [Key()];
            await ObserveStableAsync(controller);
            Equal(0, recovered);
            Commands(fake, UsbipdCommand.Detach);
        }),
        ("Verification: state query failures remain errors instead of being silently retried", async () =>
        {
            var fake = new FakeClient(Key(attached: true));
            fake.OnRead = () => { if (fake.Commands.Count > 0) throw new UsbipdException("Cannot read state."); };
            var controller = Controller(fake);
            var failure = await Throws<UsbipdException>(() => controller.ExecuteAsync(SwitchAction.Detach));
            Equal(false, failure is SwitchVerificationTimeoutException);
            Equal("Cannot read state.", failure.Message);
            Equal(ConnectionState.Error, controller.Current.State);
            Equal<SwitchVerificationTimeoutException?>(null, controller.PendingVerification);
        })
    ];

    private static SwitchController Controller(FakeClient fake) => new(fake, verification: FastOptions);

    private static async Task<(FakeClient Fake, SwitchController Controller, SwitchVerificationTimeoutException Timeout)> TimedOutDetach()
    {
        var fake = new FakeClient(Key(attached: true)) { ApplyChanges = false };
        fake.OnCommand = (_, _, _) => { fake.Devices = []; return Task.CompletedTask; };
        var controller = new SwitchController(fake, verification: TimeoutOptions);
        var timeout = await Throws<SwitchVerificationTimeoutException>(() => controller.ExecuteAsync(SwitchAction.Detach));
        Equal(ConnectionState.NotConnected, controller.Current.State);
        Equal(timeout, controller.PendingVerification);
        return (fake, controller, timeout);
    }

    private static async Task ObserveStableAsync(SwitchController controller)
    {
        await controller.RefreshAsync();
        await Task.Delay(FastOptions.StabilityPeriod * 2);
        await controller.RefreshAsync();
    }

    private static UsbDevice Key(bool shared = true, bool attached = false) => new()
    {
        InstanceId = @"USB\VID_1050&PID_0407\first", BusId = "4-4",
        PersistedGuid = shared || attached ? Guid.Parse("11111111-1111-1111-1111-111111111111") : null,
        ClientIPAddress = attached ? "172.22.0.2" : null
    };
    private static UsbDevice Other(bool attached = false) => Key(attached: attached) with
    {
        InstanceId = @"USB\VID_1050&PID_0407\second", BusId = "6-1"
    };
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}");
    }
    private static void Commands(FakeClient fake, params UsbipdCommand[] expected) =>
        Equal(string.Join(',', expected), string.Join(',', fake.Commands.Select(command => command.Command)));
    private static async Task<T> Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T exception) { return exception; }
        throw new Exception($"Expected {typeof(T).Name}");
    }
}
