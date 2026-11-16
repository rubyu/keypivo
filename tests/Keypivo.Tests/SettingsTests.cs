using System.Drawing;
using Keypivo.Core;

internal static class SettingsTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> All =>
    [
        ("Filter: custom VID, optional PID, exact matching and first-device order", () =>
        {
            var first = Device("ABCD", "0001", "2-1");
            var second = Device("ABCD", "0002", "1-1");
            var devices = new[] { Device("1050", "0407", "4-4"), first, second,
                Device("ABCD", "0002", "3-1") with { BusId = null } };
            var any = DeviceSnapshot.FromDevices(devices, new DeviceFilter("abcd"));
            Equal("2-1", any.Device?.BusId);
            Equal(2, any.MatchingDeviceCount);
            var exact = DeviceSnapshot.FromDevices(devices, new DeviceFilter("abcd", "0002"));
            Equal("1-1", exact.Device?.BusId);
            Equal(1, exact.MatchingDeviceCount);
            Equal(ConnectionState.NotConnected, DeviceSnapshot.FromDevices(devices, new DeviceFilter("ABCD", "0003")).State);
            return Task.CompletedTask;
        }),
        ("Settings: normalize hex, preserve zeros and reject malformed IDs", async () =>
        {
            var normalized = new AppSettings { VendorId = " 00ab ", ProductId = " 000f ", Position = DisplayPosition.Center }.Normalize();
            Equal("00AB", normalized.VendorId);
            Equal("000F", normalized.ProductId);
            Equal("", new AppSettings { ProductId = " " }.Normalize().ProductId);
            foreach (var invalid in new[] { "", "123", "12345", "0x1050", "G050", "10 0", "1050\n--all" })
                await Throws<ArgumentException>(() => Task.FromResult(new DeviceFilter(invalid)));
            foreach (var invalid in new[] { "123", "ZZZZ", "00000", "0x01" })
                await Throws<ArgumentException>(() => Task.FromResult(new DeviceFilter("1050", invalid)));
            await Throws<ArgumentException>(() => Task.FromResult(new AppSettings { Position = (DisplayPosition)99 }.Normalize()));
        }),
        ("Settings: defaults, save/restart persistence and atomic replacement", () => WithDirectory((store, path) =>
        {
            var initial = store.Load();
            Equal(new AppSettings(), initial.Settings);
            Equal(false, initial.RecoveredFromError);
            store.Save(new AppSettings { VendorId = "00ab", ProductId = "000f", Position = DisplayPosition.Center });
            var loaded = new SettingsStore(path).Load();
            Equal(new AppSettings { VendorId = "00AB", ProductId = "000F", Position = DisplayPosition.Center }, loaded.Settings);
            Equal(false, loaded.RecoveredFromError);
            store.Save(new AppSettings());
            Equal(new AppSettings(), store.Load().Settings);
            Equal(1, Directory.GetFiles(Path.GetDirectoryName(path)!).Length);
            return Task.CompletedTask;
        })),
        ("Settings: corrupt files fall back without overwriting user data", () => WithDirectory((store, path) =>
        {
            foreach (var json in new[] { "broken", "null", "{\"VendorId\":null}", "{\"VendorId\":\"123\"}",
                "{\"ProductId\":\"INVALID\"}", "{\"Position\":\"Elsewhere\"}", "{\"Position\":99}" })
            {
                File.WriteAllText(path, json);
                var loaded = store.Load();
                Equal(true, loaded.RecoveredFromError);
                Equal(new AppSettings(), loaded.Settings);
                Equal(json, File.ReadAllText(path));
            }
            return Task.CompletedTask;
        })),
        ("Settings: partial files retain defaults", () => WithDirectory((store, path) =>
        {
            File.WriteAllText(path, "{\"ProductId\":\"0407\"}");
            var loaded = store.Load();
            Equal(new AppSettings { ProductId = "0407" }, loaded.Settings);
            Equal(false, loaded.RecoveredFromError);
            store.Save(loaded.Settings);
            Equal("1050", store.Load().Settings.VendorId);
            return Task.CompletedTask;
        })),
        ("Settings: rejected saves keep the existing configuration", () => WithDirectory(async (store, path) =>
        {
            store.Save(new AppSettings { ProductId = "0407" });
            var before = File.ReadAllText(path);
            await Throws<ArgumentException>(() => { store.Save(new AppSettings { VendorId = "bad" }); return Task.CompletedTask; });
            Equal(before, File.ReadAllText(path));
            var impossible = new SettingsStore(Path.Combine(path, "child.json"));
            await Throws<IOException>(() => { impossible.Save(new AppSettings()); return Task.CompletedTask; });
            Equal(before, File.ReadAllText(path));
        })),
        ("Settings: upgrade reads the previous configuration and saves only to the new location", () => WithDirectory((_, path) =>
        {
            var previousPath = Path.Combine(Path.GetDirectoryName(path)!, "previous-settings.json");
            var expected = new AppSettings { VendorId = "ABCD", ProductId = "0001", Position = DisplayPosition.Center };
            new SettingsStore(previousPath).Save(expected);
            var original = File.ReadAllText(previousPath);
            var upgraded = new SettingsStore(path, previousPath);
            var loaded = upgraded.Load();
            Equal(expected, loaded.Settings);
            Equal(false, loaded.RecoveredFromError);
            Equal(false, File.Exists(path));
            upgraded.Save(expected with { ProductId = "0002" });
            Equal("0002", new SettingsStore(path).Load().Settings.ProductId);
            Equal(original, File.ReadAllText(previousPath));
            return Task.CompletedTask;
        })),
        ("Settings: new configuration takes precedence even when it is corrupt", () => WithDirectory((store, path) =>
        {
            var previousPath = Path.Combine(Path.GetDirectoryName(path)!, "previous-settings.json");
            new SettingsStore(previousPath).Save(new AppSettings { ProductId = "0407" });
            store.Save(new AppSettings { ProductId = "0002" });
            var upgraded = new SettingsStore(path, previousPath);
            Equal("0002", upgraded.Load().Settings.ProductId);
            File.WriteAllText(path, "broken");
            var loaded = upgraded.Load();
            Equal(true, loaded.RecoveredFromError);
            Equal(new AppSettings(), loaded.Settings);
            Equal("broken", File.ReadAllText(path));
            Equal("0407", new SettingsStore(previousPath).Load().Settings.ProductId);
            return Task.CompletedTask;
        })),
        ("Controller: changing filters does not mutate USB and the next action targets the new key", async () =>
        {
            var fake = new FakeClient(Device("1050", "0407", "4-4"), Device("ABCD", "0001", "2-1"));
            var controller = new SwitchController(fake, verification: SwitchVerificationTests.FastOptions);
            await controller.RefreshAsync();
            Equal("4-4", controller.Current.Device?.BusId);
            await controller.UpdateFilterAsync(new DeviceFilter("ABCD", "0001"));
            Equal(0, fake.Commands.Count);
            Equal("2-1", controller.Current.Device?.BusId);
            await controller.ExecuteAsync(SwitchAction.Attach);
            Equal(true, fake.Commands.All(command => command.BusId == "2-1"));
            Equal(ConnectionState.Attached, controller.Current.State);
        }),
        ("Controller: a nonmatching PID prevents all USB operations", async () =>
        {
            var fake = new FakeClient(Device("1050", "0407", "4-4"));
            var controller = new SwitchController(fake, filter: new DeviceFilter("1050", "0001"));
            await Throws<UsbipdException>(() => controller.ExecuteAsync(SwitchAction.Toggle));
            Equal(0, fake.Commands.Count);
            Equal(false, controller.Current.CanOperate);
        }),
        ("Controller: filter changes are rejected during an operation", async () =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var fake = new FakeClient(Device("1050", "0407", "4-4") with { PersistedGuid = Guid.NewGuid() })
            {
                OnCommand = async (_, _, token) => { entered.SetResult(); await release.Task.WaitAsync(token); }
            };
            var controller = new SwitchController(fake, verification: SwitchVerificationTests.FastOptions);
            var operation = controller.ExecuteAsync(SwitchAction.Attach);
            await entered.Task;
            try
            {
                await Throws<UsbipdException>(() => controller.UpdateFilterAsync(new DeviceFilter("ABCD")));
                Equal(DeviceFilter.Default, controller.Filter);
            }
            finally { release.SetResult(); }
            await operation;
        }),
        ("Placement: primary working area, center, negative coordinates and clamping", () =>
        {
            var area = new Rectangle(0, 0, 1920, 1040);
            var size = new Size(320, 350);
            Equal(new Point(1584, 674), WindowPlacement.Calculate(area, size, DisplayPosition.BottomRight));
            Equal(new Point(800, 345), WindowPlacement.Calculate(area, size, DisplayPosition.Center));
            Equal(new Point(-336, 674), WindowPlacement.Calculate(new Rectangle(-1920, 0, 1920, 1040), size, DisplayPosition.BottomRight));
            Equal(new Point(0, 0), WindowPlacement.Calculate(new Rectangle(0, 0, 300, 200), size, DisplayPosition.Center));
            return Task.CompletedTask;
        })
    ];

    private static UsbDevice Device(string vendor, string product, string bus) => new()
    {
        InstanceId = $@"USB\VID_{vendor}&PID_{product}\{bus}", Description = "Security key", BusId = bus
    };

    private static async Task WithDirectory(Func<SettingsStore, string, Task> action)
    {
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        var directory = Path.GetFullPath(Path.Combine(tempRoot, "Keypivo.Tests." + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            await action(new SettingsStore(path), path);
        }
        finally
        {
            if (!string.Equals(Path.GetDirectoryName(directory)?.TrimEnd(Path.DirectorySeparatorChar),
                tempRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to remove a directory outside the test temp root.");
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}");
    }

    private static async Task Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}");
    }
}
