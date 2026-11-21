using Keypivo.Core;

internal static class ManualSetupTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> All =>
    [
        ("Setup: missing usbipd opens installation without attempting USB commands", async () =>
        {
            var fake = new FakeClient { OnRead = () => throw new UsbipdNotFoundException() };
            var controller = new SwitchController(fake);
            var required = await Throws<ManualSetupRequiredException>(() => controller.ExecuteAsync(SwitchAction.Toggle));
            Equal(SetupKind.Install, required.Setup.Kind);
            Equal(ManualSetup.InstallCommand, required.Setup.Command);
            var result = await controller.CheckSetupAsync(required.Setup);
            Equal(false, result.IsComplete);
            Equal(true, result.CanCopy);
            Equal(0, fake.Commands.Count);
        }),
        ("Setup: installation completes without a connected key and returns current device state", async () =>
        {
            var fake = new FakeClient();
            var controller = new SwitchController(fake);
            var result = await controller.CheckSetupAsync(new(SetupKind.Install));
            Equal(true, result.IsComplete);
            Equal(ConnectionState.NotConnected, controller.Current.State);
            Equal(0, fake.Commands.Count);
        }),
        ("Setup: a failed state query does not confirm installation", async () =>
        {
            var fake = new FakeClient { OnRead = () => throw new UsbipdException("Broken state") };
            var controller = new SwitchController(fake);
            var setup = new ManualSetup(SetupKind.Install);
            Equal(false, (await controller.CheckSetupAsync(setup)).IsComplete);
            Equal(ConnectionState.Error, controller.Current.State);
            fake.OnRead = null;
            Equal(true, (await controller.CheckSetupAsync(setup)).IsComplete);
        }),
        ("Setup: manual sharing is confirmed without automatically attaching", async () =>
        {
            var fake = new FakeClient(Key());
            var controller = new SwitchController(fake);
            var required = await Throws<ManualSetupRequiredException>(() => controller.ExecuteAsync(SwitchAction.Attach));
            Equal(SetupKind.Share, required.Setup.Kind);
            Equal("usbipd bind --busid 4-4", required.Setup.Command);
            var pending = await controller.CheckSetupAsync(required.Setup);
            Equal(false, pending.IsComplete);
            Equal(true, pending.CanCopy);
            fake.Devices = [Key(shared: true)];
            Equal(true, (await controller.CheckSetupAsync(required.Setup)).IsComplete);
            Equal(ConnectionState.WindowsShared, controller.Current.State);
            Equal(0, fake.Commands.Count);
        }),
        ("Setup: reordering matches still checks the original key and leaves menu selection unchanged", async () =>
        {
            var setup = new ManualSetup(SetupKind.Share, Key());
            var first = Key() with { InstanceId = @"USB\VID_1050&PID_0407\second", BusId = "6-1" };
            var fake = new FakeClient(first, Key(shared: true) with { InstanceId = Key().InstanceId.ToLowerInvariant() });
            var controller = new SwitchController(fake);
            Equal(true, (await controller.CheckSetupAsync(setup)).IsComplete);
            Equal("6-1", controller.Current.Device?.BusId);
            Equal(ConnectionState.WindowsUnshared, controller.Current.State);
            Equal(0, fake.Commands.Count);
        }),
        ("Setup: removal, replacements, changed bus IDs and disconnected records disable copy", async () =>
        {
            var setup = new ManualSetup(SetupKind.Share, Key());
            var fake = new FakeClient();
            var controller = new SwitchController(fake);
            foreach (var devices in new UsbDevice[][] { [], [Key(shared: true) with { BusId = null }],
                [Key(shared: true) with { BusId = "6-1" }], [Key(shared: true) with { InstanceId = @"USB\VID_1050&PID_0407\replacement" }] })
            {
                fake.Devices = devices;
                var result = await controller.CheckSetupAsync(setup);
                Equal(false, result.CanCopy);
                Equal(false, result.IsComplete);
            }
            fake.Devices = [Key(shared: true)];
            Equal(true, (await controller.CheckSetupAsync(setup)).IsComplete);
            Equal(0, fake.Commands.Count);
        }),
        ("Setup: unsharing requires the original key to be detached, connected and no longer forced", async () =>
        {
            var setup = new ManualSetup(SetupKind.Unshare, Key(shared: true));
            Equal("usbipd unbind --busid 4-4", setup.Command);
            var fake = new FakeClient(Key(shared: true, attached: true));
            var controller = new SwitchController(fake);
            var attached = await controller.CheckSetupAsync(setup);
            Equal(false, attached.CanCopy);
            Equal(false, attached.IsComplete);
            fake.Devices = [Key(shared: true)];
            var shared = await controller.CheckSetupAsync(setup);
            Equal(true, shared.CanCopy);
            Equal(false, shared.IsComplete);
            fake.Devices = [Key() with { IsForced = true }];
            Equal(false, (await controller.CheckSetupAsync(setup)).IsComplete);
            fake.Devices = [];
            Equal(false, (await controller.CheckSetupAsync(setup)).IsComplete);
            fake.Devices = [Key()];
            Equal(true, (await controller.CheckSetupAsync(setup)).IsComplete);
            Equal(ConnectionState.WindowsUnshared, controller.Current.State);
            Equal(0, fake.Commands.Count);
        }),
        ("Setup: read failures and a removed tool prevent copying device commands", async () =>
        {
            var setup = new ManualSetup(SetupKind.Share, Key());
            var fake = new FakeClient(Key());
            var controller = new SwitchController(fake);
            foreach (var error in new UsbipdException[] { new UsbipdNotFoundException(), new("Read failed") })
            {
                fake.OnRead = () => throw error;
                var result = await controller.CheckSetupAsync(setup);
                Equal(false, result.CanCopy);
                Equal(false, result.IsComplete);
            }
        }),
        ("Setup: invalid bus IDs cannot enter clipboard commands", () =>
        {
            foreach (var bus in new string?[] { null, "", "4-4; whoami", "4-4\n", "4-4 --all" })
            {
                try { _ = new ManualSetup(SetupKind.Share, Key() with { BusId = bus }); }
                catch (ArgumentException) { continue; }
                throw new Exception("Invalid bus ID accepted");
            }
            return Task.CompletedTask;
        }),
        ("Setup: changing the filter invalidates the old target", async () =>
        {
            var fake = new FakeClient(Key(shared: true));
            var controller = new SwitchController(fake);
            var setup = new ManualSetup(SetupKind.Share, Key());
            await controller.UpdateFilterAsync(new("ABCD"));
            var result = await controller.CheckSetupAsync(setup);
            Equal(false, result.CanCopy);
            Equal(false, result.IsComplete);
            Equal(0, fake.Commands.Count);
        }),
        ("Setup: checks exclude overlapping commands and release their lock on cancellation", async () =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var fake = new FakeClient(Key(shared: true));
            fake.OnReadAsync = async token => { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); };
            var controller = new SwitchController(fake);
            var setup = new ManualSetup(SetupKind.Share, Key());
            using var cancel = new CancellationTokenSource();
            var check = controller.CheckSetupAsync(setup, cancel.Token);
            await entered.Task;
            await Throws<UsbipdException>(() => controller.ExecuteAsync(SwitchAction.Attach));
            await Throws<UsbipdException>(() => controller.CheckSetupAsync(setup));
            await Throws<UsbipdException>(() => controller.UpdateFilterAsync(new("ABCD")));
            cancel.Cancel();
            await Throws<OperationCanceledException>(async () => await check);
            fake.OnReadAsync = null;
            Equal(true, (await controller.CheckSetupAsync(setup)).IsComplete);
            Equal(0, fake.Commands.Count);
        })
    ];

    private static UsbDevice Key(bool shared = false, bool attached = false) => new()
    {
        InstanceId = @"USB\VID_1050&PID_0407\first", BusId = "4-4",
        PersistedGuid = shared || attached ? Guid.Parse("11111111-1111-1111-1111-111111111111") : null,
        ClientIPAddress = attached ? "172.22.0.2" : null
    };
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}");
    }
    private static async Task<T> Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T exception) { return exception; }
        throw new Exception($"Expected {typeof(T).Name}");
    }
}
