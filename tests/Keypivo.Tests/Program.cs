using System.Reflection;
using System.Text.Json;
using Keypivo.Core;

if (args.Contains("--emit"))
{
    Console.Write(new string('o', 128 * 1024));
    Console.Error.Write(new string('e', 128 * 1024));
    return 7;
}
if (args.Contains("--sleep"))
{
    await Task.Delay(TimeSpan.FromSeconds(30));
    return 0;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("JSON: first connected VID 1050, preserving enumeration order", () =>
    {
        var json = """
            {"Devices":[
              {"InstanceId":"USB\\VID_1050&PID_0407\\old","BusId":null,"PersistedGuid":"11111111-1111-1111-1111-111111111111","ClientIPAddress":null},
              {"InstanceId":"USB\\VID_1051&PID_0407\\other","BusId":"1-1","PersistedGuid":null,"ClientIPAddress":null},
              {"InstanceId":"usb\\vid_1050&pid_0407\\first","BusId":"4-4","PersistedGuid":null,"ClientIPAddress":null,"Description":"Security key"},
              {"InstanceId":"USB\\VID_1050&PID_0407\\second","BusId":"1-2","PersistedGuid":null,"ClientIPAddress":null}
            ]}
            """;
        var snapshot = DeviceSnapshot.FromDevices(UsbipdStateParser.Parse(json));
        Equal("4-4", snapshot.Device?.BusId);
        Equal(2, snapshot.MatchingDeviceCount);
        Equal(ConnectionState.WindowsUnshared, snapshot.State);
        return Task.CompletedTask;
    }),
    ("JSON: reject unsupported or incomplete state instead of treating it as Windows", async () =>
    {
        foreach (var json in new[] { "broken", "{}", "null", "{\"Devices\":null}", "{\"Devices\":[null]}",
            "{\"Devices\":[{\"InstanceId\":\"USB\\\\VID_1050&PID_0407\\\\a\",\"BusId\":\"4-4\"}]}" })
            await Throws<UsbipdException>(() => Task.FromResult(UsbipdStateParser.Parse(json)));
    }),
    ("JSON: exact vendor matching, invalid bus IDs, persisted and empty lists", () =>
    {
        var devices = new[] { Key() with { InstanceId = @"USB\VID_10500&PID_0407\wrong" },
            Key() with { BusId = null }, Key() with { BusId = "4-4 --all" }, Key() with { BusId = "4-4\n" },
            Key() with { InstanceId = @"HID\VID_1050&PID_0407\child" } };
        Equal(ConnectionState.NotConnected, DeviceSnapshot.FromDevices(devices).State);
        Equal(ConnectionState.NotConnected, DeviceSnapshot.FromDevices(UsbipdStateParser.Parse("{\"Devices\":[]}")).State);
        return Task.CompletedTask;
    }),
    ("State: shared is Windows; forced sharing and attachment are distinct", () =>
    {
        Equal(ConnectionState.WindowsShared, DeviceSnapshot.FromDevices([Key(shared: true)]).State);
        Equal(ConnectionState.ForcedShared, DeviceSnapshot.FromDevices([Key(shared: true) with { IsForced = true }]).State);
        Equal(ConnectionState.Attached, DeviceSnapshot.FromDevices([Key(attached: true) with { IsForced = true }]).State);
        return Task.CompletedTask;
    }),
    ("Attach: initial bind, then attach, verified state", async () =>
    {
        var fake = new FakeClient(Key());
        var controller = Controller(fake);
        await controller.ExecuteAsync(SwitchAction.Attach);
        Commands(fake, UsbipdCommand.Bind, UsbipdCommand.Attach);
        Equal(ConnectionState.Attached, controller.Current.State);
    }),
    ("Attach: shared device needs no bind; repeat is idempotent", async () =>
    {
        var fake = new FakeClient(Key(shared: true));
        var controller = Controller(fake);
        await controller.ExecuteAsync(SwitchAction.Attach);
        await controller.ExecuteAsync(SwitchAction.Attach);
        Commands(fake, UsbipdCommand.Attach);
    }),
    ("Detach: preserve sharing; repeat is idempotent", async () =>
    {
        var fake = new FakeClient(Key(attached: true));
        var controller = Controller(fake);
        await controller.ExecuteAsync(SwitchAction.Detach);
        await controller.ExecuteAsync(SwitchAction.Detach);
        Commands(fake, UsbipdCommand.Detach);
        Equal(ConnectionState.WindowsShared, controller.Current.State);
    }),
    ("Toggle: re-read state after external change", async () =>
    {
        var fake = new FakeClient(Key(shared: true));
        var controller = Controller(fake);
        await controller.RefreshAsync();
        fake.Devices = [Key(attached: true)];
        await controller.ExecuteAsync(SwitchAction.Toggle);
        await controller.ExecuteAsync(SwitchAction.Toggle);
        Commands(fake, UsbipdCommand.Detach, UsbipdCommand.Attach);
        Equal(ConnectionState.Attached, controller.Current.State);
    }),
    ("Unbind: detach first and remove sharing", async () =>
    {
        var fake = new FakeClient(Key(attached: true));
        var controller = Controller(fake);
        await controller.ExecuteAsync(SwitchAction.Unshare);
        Commands(fake, UsbipdCommand.Detach, UsbipdCommand.Unbind);
        Equal(ConnectionState.WindowsUnshared, controller.Current.State);
    }),
    ("UAC canceled: no attach and state retained", async () =>
    {
        var fake = new FakeClient(Key()) { OnCommand = (_, _, _) => throw new UsbipdException("The administrator permission request was canceled.") };
        var controller = Controller(fake);
        await Throws<UsbipdException>(() => controller.ExecuteAsync(SwitchAction.Attach));
        Commands(fake, UsbipdCommand.Bind);
        Equal(ConnectionState.WindowsUnshared, controller.Current.State);
    }),
    ("Failed attach: refresh partial result, never report attachment", async () =>
    {
        var fake = new FakeClient(Key())
        {
            OnCommand = (command, _, _) => command == UsbipdCommand.Attach
                ? throw new UsbipdException("WSL is not running") : Task.CompletedTask
        };
        var controller = Controller(fake);
        await Throws<UsbipdException>(() => controller.ExecuteAsync(SwitchAction.Attach));
        Equal(ConnectionState.WindowsShared, controller.Current.State);
    }),
    ("False success: command exit zero without state change is an error", async () =>
    {
        var fake = new FakeClient(Key(shared: true)) { ApplyChanges = false };
        var controller = Controller(fake);
        await Throws<UsbipdException>(() => controller.ExecuteAsync(SwitchAction.Attach));
        Equal(ConnectionState.WindowsShared, controller.Current.State);
    }),
    ("Delayed state: allow usbipd state to settle", async () =>
    {
        var fake = new FakeClient(Key(shared: true)) { ApplyChanges = false };
        var readsAfterAttach = 0;
        fake.OnRead = () =>
        {
            if (fake.Commands.Count > 0 && ++readsAfterAttach == 2) fake.Devices = [Key(attached: true)];
        };
        var controller = Controller(fake);
        await controller.ExecuteAsync(SwitchAction.Attach);
        Equal(ConnectionState.Attached, controller.Current.State);
    }),
    ("Unplug during bind: never switch to a second security key", async () =>
    {
        var second = Key() with { InstanceId = @"USB\VID_1050&PID_0407\second", BusId = "6-1" };
        var fake = new FakeClient(Key(), second) { ApplyChanges = false };
        fake.OnCommand = (_, _, _) => { fake.Devices = [second]; return Task.CompletedTask; };
        var controller = Controller(fake);
        await Throws<UsbipdException>(() => controller.ExecuteAsync(SwitchAction.Attach));
        Commands(fake, UsbipdCommand.Bind);
        Equal(ConnectionState.NotConnected, controller.Current.State);
        await controller.RefreshAsync();
        Equal("6-1", controller.Current.Device?.BusId);
    }),
    ("No device / missing executable / broken state: disable operations and recover", async () =>
    {
        var fake = new FakeClient();
        var controller = Controller(fake);
        await Throws<UsbipdException>(() => controller.ExecuteAsync(SwitchAction.Toggle));
        Equal(0, fake.Commands.Count);
        fake.OnRead = () => throw new UsbipdNotFoundException();
        await controller.RefreshAsync();
        Equal(ConnectionState.ToolMissing, controller.Current.State);
        fake.OnRead = () => throw new UsbipdException("bad JSON");
        await controller.RefreshAsync();
        Equal(ConnectionState.Error, controller.Current.State);
        fake.OnRead = null;
        fake.Devices = [Key()];
        await controller.RefreshAsync();
        Equal(ConnectionState.WindowsUnshared, controller.Current.State);
    }),
    ("Concurrency: skip polls and reject duplicate operations during switching", async () =>
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = new FakeClient(Key(shared: true))
        {
            OnCommand = async (_, _, token) => { entered.SetResult(); await release.Task.WaitAsync(token); }
        };
        var controller = Controller(fake);
        var first = controller.ExecuteAsync(SwitchAction.Attach);
        await entered.Task;
        try
        {
            var readCount = fake.Reads;
            await controller.RefreshAsync();
            Equal(readCount, fake.Reads);
            await Throws<UsbipdException>(() => controller.ExecuteAsync(SwitchAction.Toggle));
        }
        finally { release.SetResult(); }
        await first;
        Commands(fake, UsbipdCommand.Attach);
    }),
    ("Cancellation: release operation lock for next refresh", async () =>
    {
        var fake = new FakeClient(Key(shared: true)) { OnCommand = (_, _, token) => Task.Delay(30000, token) };
        var controller = Controller(fake);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Throws<OperationCanceledException>(() => controller.ExecuteAsync(SwitchAction.Attach, cancel.Token));
        fake.OnCommand = null;
        await controller.RefreshAsync();
        Equal(ConnectionState.WindowsShared, controller.Current.State);
    }),
    ("CLI contract: exact arguments and elevation only for bind/unbind", async () =>
    {
        var runner = new FakeRunner();
        var client = new UsbipdClient(runner, () => @"C:\Program Files\usbipd-win\usbipd.exe");
        foreach (var command in Enum.GetValues<UsbipdCommand>()) await client.ExecuteAsync(command, "4-4", default);
        Equal("bind --busid 4-4|attach --wsl --busid 4-4|detach --busid 4-4|unbind --busid 4-4",
            string.Join('|', runner.Requests.Select(r => string.Join(' ', r.Arguments))));
        Equal("True,False,False,True", string.Join(',', runner.Requests.Select(r => r.Elevated)));
        await Throws<UsbipdException>(() => client.ExecuteAsync(UsbipdCommand.Bind, "4-4 --all", default));
        Equal(4, runner.Requests.Count);
        runner.Result = new(1, "", "service unavailable");
        await Throws<UsbipdException>(() => client.GetDevicesAsync(default));
    }),
    ("Process: drain both output streams and preserve nonzero exit code", async () =>
    {
        var result = await new ProcessCommandRunner().RunAsync(ChildRequest("--emit", TimeSpan.FromSeconds(10)), default);
        Equal(7, result.ExitCode);
        Equal(128 * 1024, result.StandardOutput.Length);
        Equal(128 * 1024, result.StandardError.Length);
    }),
    ("Process: timeout and explicit cancellation terminate the wait", async () =>
    {
        var runner = new ProcessCommandRunner();
        await Throws<UsbipdException>(() => runner.RunAsync(ChildRequest("--sleep", TimeSpan.FromMilliseconds(200)), default));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Throws<OperationCanceledException>(() => runner.RunAsync(ChildRequest("--sleep", TimeSpan.FromSeconds(10)), cancel.Token));
    })
};

var failures = 0;
var allTests = tests.Concat(SettingsTests.All).Concat(SwitchVerificationTests.All).ToArray();
foreach (var test in allTests)
{
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception exception) { failures++; Console.WriteLine($"FAIL {test.Name}\n{exception}"); }
}
Console.WriteLine($"{allTests.Length - failures}/{allTests.Length} passed");
return failures == 0 ? 0 : 1;

static UsbDevice Key(bool shared = false, bool attached = false) => new()
{
    InstanceId = @"USB\VID_1050&PID_0407\first", Description = "Security key", BusId = "4-4",
    PersistedGuid = shared || attached ? Guid.Parse("11111111-1111-1111-1111-111111111111") : null,
    ClientIPAddress = attached ? "172.22.0.2" : null
};
static SwitchController Controller(FakeClient fake) => new(fake, verification: SwitchVerificationTests.FastOptions);
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}");
}
static void Commands(FakeClient fake, params UsbipdCommand[] expected) =>
    Equal(string.Join(',', expected), string.Join(',', fake.Commands.Select(c => c.Command)));
static async Task Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}
static CommandRequest ChildRequest(string argument, TimeSpan timeout)
{
    var executable = Environment.ProcessPath!;
    string[] arguments = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        ? [Assembly.GetExecutingAssembly().Location, argument] : [argument];
    return new(executable, arguments, false, timeout);
}

sealed class FakeClient(params UsbDevice[] initial) : IUsbipdClient
{
    public UsbDevice[] Devices { get; set; } = initial;
    public List<(UsbipdCommand Command, string BusId)> Commands { get; } = [];
    public int Reads { get; private set; }
    public bool ApplyChanges { get; init; } = true;
    public Action? OnRead { get; set; }
    public Func<CancellationToken, Task>? OnReadAsync { get; set; }
    public Func<UsbipdCommand, string, CancellationToken, Task>? OnCommand { get; set; }
    public async Task<IReadOnlyList<UsbDevice>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Reads++;
        if (OnReadAsync is not null) await OnReadAsync(cancellationToken);
        OnRead?.Invoke();
        return Devices;
    }
    public async Task ExecuteAsync(UsbipdCommand command, string busId, CancellationToken cancellationToken)
    {
        Commands.Add((command, busId));
        if (OnCommand is not null) await OnCommand(command, busId, cancellationToken);
        if (!ApplyChanges) return;
        Devices = Devices.Select(d => d.BusId != busId ? d : command switch
        {
            UsbipdCommand.Bind => d with { PersistedGuid = Guid.Parse("11111111-1111-1111-1111-111111111111") },
            UsbipdCommand.Attach => d with { ClientIPAddress = "172.22.0.2" },
            UsbipdCommand.Detach => d with { ClientIPAddress = null },
            UsbipdCommand.Unbind => d with { PersistedGuid = null, IsForced = false },
            _ => d
        }).ToArray();
    }
}
sealed class FakeRunner : ICommandRunner
{
    public List<CommandRequest> Requests { get; } = [];
    public CommandResult Result { get; set; } = new(0, JsonSerializer.Serialize(new { Devices = Array.Empty<object>() }), "");
    public Task<CommandResult> RunAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(Result);
    }
}
