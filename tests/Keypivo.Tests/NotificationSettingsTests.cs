using Keypivo.Core;

internal static class NotificationSettingsTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> All =>
    [
        ("Notifications: individual choices and master switch are independent", () =>
        {
            var defaults = new NotificationSettings();
            foreach (var kind in Enum.GetValues<NotificationKind>())
            {
                Assert(defaults.Allows(kind), "All notifications default to on");
                var changed = defaults.WithSelection(kind, false);
                foreach (var other in Enum.GetValues<NotificationKind>())
                    Assert(changed.Allows(other) == (other != kind), "A choice must affect only its own kind");
                var muted = changed with { Enabled = false };
                Assert(Enum.GetValues<NotificationKind>().All(k => !muted.Allows(k)), "Master must mute every kind");
                Assert((muted with { Enabled = true }) == changed, "Unmuting must retain individual choices");
            }
            Assert(!defaults.Allows((NotificationKind)99), "Unknown notifications must not be sent");
            return Task.CompletedTask;
        }),
        ("Notifications: state mapping identifies destinations and ignores transient checking", () =>
        {
            var expected = new Dictionary<ConnectionState, NotificationKind?>
            {
                [ConnectionState.Checking] = null,
                [ConnectionState.ToolMissing] = NotificationKind.SetupRequired,
                [ConnectionState.Error] = NotificationKind.StatusUnavailable,
                [ConnectionState.NotConnected] = NotificationKind.KeyDisconnected,
                [ConnectionState.WindowsUnshared] = NotificationKind.AvailableOnWindows,
                [ConnectionState.WindowsShared] = NotificationKind.AvailableOnWindows,
                [ConnectionState.ForcedShared] = NotificationKind.WaitingForConnection,
                [ConnectionState.Attached] = NotificationKind.ConnectedToWsl
            };
            foreach (var state in Enum.GetValues<ConnectionState>())
                Assert(NotificationSettings.ForState(state) == expected[state], "Incorrect destination mapping");
            return Task.CompletedTask;
        }),
        ("Notifications: save/restart, migration, partial preferences and recovery", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "keypivo-notifications-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var store = new SettingsStore(path);
                foreach (var kind in Enum.GetValues<NotificationKind>())
                {
                    var settings = new AppSettings { Notifications = new NotificationSettings().WithSelection(kind, false) with { Enabled = false } };
                    store.Save(settings);
                    Assert(new SettingsStore(path).Load().Settings == settings, "Choices must survive a restart");
                }
                foreach (var json in new[] { "{}", "{\"VendorId\":\"1050\"}", "{\"Notifications\":null}" })
                {
                    File.WriteAllText(path, json);
                    Assert(store.Load() == new SettingsLoadResult(new(), false), "Older and null settings must retain defaults");
                }
                File.WriteAllText(path, "{\"Notifications\":{\"ConnectedToWsl\":false}}");
                Assert(store.Load().Settings.Notifications == new NotificationSettings { ConnectedToWsl = false },
                    "Missing individual choices default to on");
                const string invalidDevice = "{\"VendorId\":\"bad\",\"Notifications\":{\"SettingsCouldNotBeLoaded\":false}}";
                File.WriteAllText(path, invalidDevice);
                var recovered = store.Load();
                Assert(recovered.RecoveredFromError && !recovered.Settings.Notifications.SettingsCouldNotBeLoaded,
                    "Device-setting recovery must respect readable notification preferences");
                Assert(File.ReadAllText(path) == invalidDevice, "Recovery must not overwrite user data");
                File.WriteAllText(path, "{\"Notifications\":{\"Enabled\":\"invalid\"}}");
                Assert(store.Load() == new SettingsLoadResult(new(), true), "Malformed choices must fall back safely");
            }
            finally { File.Delete(path); }
            return Task.CompletedTask;
        })
    ];

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
