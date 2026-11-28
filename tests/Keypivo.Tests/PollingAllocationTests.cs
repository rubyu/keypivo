using System.Text.Json;
using System.Text.RegularExpressions;
using Keypivo.Core;

internal static class PollingAllocationTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> All =>
    [
        ("IDs: parse on assignment, preserve exact matching and record cloning", () =>
        {
            var pattern = new Regex(@"\AUSB\\VID_([0-9A-F]{4})&PID_([0-9A-F]{4})(?:[&\\]|\z)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var source = new UsbDevice { InstanceId = @"USB\VID_1050&PID_0407\key", BusId = "4-4" };
            foreach (var id in new[] { "", "USB", @"usb\vid_abCd&pid_0eF0", @"USB\VID_1050&PID_0407&MI_01",
                @"USB\VID_10500&PID_0407\bad", @"USB\VID_1050&PID_04070\bad", @"HID\VID_1050&PID_0407\bad",
                @"USB\VID_1050&PID_0407" + "\n", @"USB\VID_Ｇ050&PID_0407", @"USB\VID_1050&PID_0407\key" })
            {
                var device = source with { InstanceId = id };
                var match = pattern.Match(id);
                Equal(match.Groups[1].Value.ToUpperInvariant(), device.VendorId);
                Equal(match.Groups[2].Value.ToUpperInvariant(), device.ProductId);
                Equal("1050", source.VendorId);
                Equal("0407", source.ProductId);
                Equal(device, source with { InstanceId = id });
            }
            Equal("", (source with { InstanceId = null! }).VendorId);
            return Task.CompletedTask;
        }),
        ("Polling: identical fresh responses reuse immutable parsed devices", async () =>
        {
            var key = new UsbDevice { InstanceId = @"USB\VID_1050&PID_0407\key", BusId = "4-4" };
            var runner = new SequenceRunner { Result = State(key) };
            var client = new UsbipdClient(runner, () => "fixture");
            var first = await client.GetDevicesAsync(default);
            var second = await client.GetDevicesAsync(default);
            Equal(2, runner.Reads);
            Equal(true, ReferenceEquals(first, second));
            try { ((IList<UsbDevice>)second)[0] = key with { BusId = "9-9" }; throw new Exception("Cached state was mutable"); }
            catch (NotSupportedException) { }

            foreach (var changed in new[] { key with { Description = "Changed name" }, key with { BusId = "4-5" },
                key with { PersistedGuid = Guid.NewGuid() }, key with { IsForced = true },
                key with { ClientIPAddress = "172.22.0.2" }, key with { InstanceId = @"USB\VID_1050&PID_0407\replacement" } })
            {
                runner.Result = State(changed);
                var current = await client.GetDevicesAsync(default);
                Equal(changed, current[0]);
                Equal(false, ReferenceEquals(first, current));
            }
            runner.Result = State();
            Equal(0, (await client.GetDevicesAsync(default)).Count);
        }),
        ("Polling: cached success never hides command errors, malformed output or cancellation", async () =>
        {
            var key = new UsbDevice { InstanceId = @"USB\VID_1050&PID_0407\key", BusId = "4-4" };
            var good = State(key);
            var runner = new SequenceRunner { Result = good };
            var client = new UsbipdClient(runner, () => "fixture");
            await client.GetDevicesAsync(default);
            foreach (var bad in new[] { good with { ExitCode = 1, StandardError = "Unavailable" }, new CommandResult(0, "invalid", "") })
            {
                runner.Result = bad;
                try { await client.GetDevicesAsync(default); throw new Exception("Bad response was hidden by cache"); }
                catch (UsbipdException) { }
            }
            runner.Result = good;
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try { await client.GetDevicesAsync(cancellation.Token); throw new Exception("Cancellation was hidden by cache"); }
            catch (OperationCanceledException) { }
            Equal(key, (await client.GetDevicesAsync(default))[0]);
        }),
        ("Selection: original target, enumeration order, and full match count survive streaming selection", () =>
        {
            var first = new UsbDevice { InstanceId = @"USB\VID_1050&PID_0407\first", BusId = "4-4" };
            var second = first with { InstanceId = @"USB\VID_1050&PID_0407\second", BusId = "4-5" };
            UsbDevice[] devices = [first with { BusId = null }, second, first, first with { InstanceId = @"USB\VID_0001&PID_0407\other" }];
            var normal = DeviceSnapshot.FromDevices(devices);
            Equal(second, normal.Device);
            Equal(2, normal.MatchingDeviceCount);
            var targeted = DeviceSnapshot.FromDevices(devices, target: first);
            Equal(first, targeted.Device);
            Equal(2, targeted.MatchingDeviceCount);
            Equal(null, DeviceSnapshot.FromDevices(devices, target: first with { BusId = "8-8" }).Device);
            return Task.CompletedTask;
        })
    ];

    private static CommandResult State(params UsbDevice[] devices) => new(0, JsonSerializer.Serialize(new { Devices = devices }), "");
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}");
    }
    private sealed class SequenceRunner : ICommandRunner
    {
        public CommandResult Result { get; set; } = State();
        public int Reads { get; private set; }
        public Task<CommandResult> RunAsync(CommandRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!request.Arguments.SequenceEqual(new[] { "state" })) throw new Exception("Unexpected command");
            Reads++;
            return Task.FromResult(Result);
        }
    }
}
