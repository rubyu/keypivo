using Windows.ApplicationModel;

namespace Keypivo.UiTests;

internal static class StartupSettingsTests
{
    public static void Run()
    {
        // CI runs unpackaged. Exercise the real identity check without touching
        // any startup registration, including when a caller requests enabling.
        var unpackaged = new StartupService();
        Assert(unpackaged.GetStatusAsync().GetAwaiter().GetResult() == StartupStatus.Unpackaged,
            "A development executable must be detected as unpackaged");
        Assert(unpackaged.SetEnabledAsync(true).GetAwaiter().GetResult() == StartupStatus.Unpackaged,
            "An unpackaged executable must not create a startup registration");
        var startup = new FakeStartupService();
        using (var form = Show(startup))
        {
            var checkbox = Find<CheckBox>(form, "StartAtSignIn");
            Assert(checkbox.Checked && checkbox.Enabled, "Default-enabled Windows task must be shown as on");
            Program.CheckBounds(form);
            Program.Capture(form, "settings-startup");
            checkbox.Checked = false;
            Assert(startup.Writes == 0, "Changing the checkbox must wait for Save");
            Find<Button>(form, "SaveSettings").PerformClick();
            Assert(startup.Status == StartupStatus.Disabled && startup.Writes == 1 && form.DialogResult == DialogResult.OK,
                "Save must disable startup");
        }
        using (var form = Show(startup))
        {
            var checkbox = Find<CheckBox>(form, "StartAtSignIn");
            Assert(!checkbox.Checked && checkbox.Enabled, "Reopening must read the disabled Windows task");
            checkbox.Checked = true;
            Find<Button>(form, "SaveSettings").PerformClick();
            Assert(startup.Status == StartupStatus.Enabled && startup.Writes == 2, "Save must enable startup again");
        }
        using (var form = Show(startup))
        {
            Find<CheckBox>(form, "StartAtSignIn").Checked = false;
            Find<Button>(form, "CancelSettings").PerformClick();
            Assert(startup.Status == StartupStatus.Enabled && startup.Writes == 2, "Cancel must not change startup");
        }

        foreach (var state in new[] { StartupStatus.DisabledByUser, StartupStatus.DisabledByPolicy,
            StartupStatus.EnabledByPolicy, StartupStatus.Unpackaged, StartupStatus.Unavailable })
        {
            var restricted = new FakeStartupService { Status = state };
            using var form = Show(restricted);
            var checkbox = Find<CheckBox>(form, "StartAtSignIn");
            Assert(!checkbox.Enabled && checkbox.Checked == state.IsEnabled(), "Restricted state must be accurate and read-only");
            Assert(Find<Label>(form, "StartupHint").Visible, "Restricted state needs a concise explanation");
            Assert(Find<LinkLabel>(form, "WindowsStartupSettings").Visible == (state == StartupStatus.DisabledByUser),
                "Windows startup settings must be offered for user-disabled tasks");
            Program.CheckBounds(form);
            if (state == StartupStatus.DisabledByUser) Program.Capture(form, "settings-startup-disabled");
            Find<Button>(form, "SaveSettings").PerformClick();
            Assert(restricted.Writes == 0 && form.DialogResult == DialogResult.OK,
                "Saving other settings must not attempt to override Windows");
        }

        var unchanged = new FakeStartupService();
        using (var form = Show(unchanged))
        {
            unchanged.Status = StartupStatus.DisabledByUser;
            Find<Button>(form, "SaveSettings").PerformClick();
            Assert(unchanged.Writes == 0 && unchanged.Status == StartupStatus.DisabledByUser,
                "Saving an unchanged checkbox must preserve a later external choice");
        }
        var changedExternally = new FakeStartupService { Status = StartupStatus.Disabled };
        using (var form = Show(changedExternally))
        {
            Find<CheckBox>(form, "StartAtSignIn").Checked = true;
            changedExternally.Status = StartupStatus.DisabledByUser;
            Find<Button>(form, "SaveSettings").PerformClick();
            Assert(form.Visible && !Find<CheckBox>(form, "StartAtSignIn").Checked
                && Find<LinkLabel>(form, "WindowsStartupSettings").Visible && Find<Label>(form, "SettingsError").Visible,
                "A rejected change must keep Settings open and show the actual Windows state");
        }
        var updateFailure = new FakeStartupService { FailWrite = true };
        var otherSettingsSaved = false;
        using (var form = Show(updateFailure, () => { otherSettingsSaved = true; return Task.CompletedTask; }))
        {
            Find<CheckBox>(form, "StartAtSignIn").Checked = false;
            Find<Button>(form, "SaveSettings").PerformClick();
            Assert(otherSettingsSaved && form.Visible && Find<Label>(form, "SettingsError").Text.Contains("Other settings were saved"),
                "Partial failure must be reported without pretending all settings succeeded");
        }
        var saveFailure = new FakeStartupService();
        using (var form = Show(saveFailure, () => throw new IOException("Settings file unavailable")))
        {
            Find<CheckBox>(form, "StartAtSignIn").Checked = false;
            Find<Button>(form, "SaveSettings").PerformClick();
            Assert(saveFailure.Writes == 0 && form.Visible && Find<Label>(form, "SettingsError").Visible,
                "A failed local save must leave the Windows startup task unchanged");
        }
        using (var form = Show(new FakeStartupService { FailRead = true }))
        {
            Assert(!Find<CheckBox>(form, "StartAtSignIn").Enabled && Find<Button>(form, "SaveSettings").Enabled,
                "A failed startup read must not block unrelated settings");
        }
        foreach (var state in Enum.GetValues<StartupTaskState>())
            Assert(StartupService.Map(state).ToString() == state.ToString(), "Windows startup states must map without conflation");
        Assert(StartupService.Map((StartupTaskState)99) == StartupStatus.Unavailable, "Unknown Windows state must not appear configurable");
        Console.WriteLine("PASS Native startup settings: on/off persistence, Save/Cancel, external choices, policy, failures, and layout.");
    }

    private static SettingsForm Show(FakeStartupService startup, Func<Task>? apply = null)
    {
        var form = new SettingsForm(new(), false, false, _ => apply?.Invoke() ?? Task.CompletedTask, startup);
        form.Show();
        Application.DoEvents();
        return form;
    }

    private static T Find<T>(Control parent, string name) where T : Control =>
        parent.Controls.Find(name, true).OfType<T>().Single();

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class FakeStartupService : IStartupService
    {
        public StartupStatus Status { get; set; } = StartupStatus.Enabled;
        public bool FailRead { get; init; }
        public bool FailWrite { get; init; }
        public int Writes { get; private set; }
        public Task<StartupStatus> GetStatusAsync() => FailRead
            ? throw new IOException("Cannot read startup") : Task.FromResult(Status);
        public Task<StartupStatus> SetEnabledAsync(bool enabled)
        {
            Writes++;
            if (FailWrite) throw new IOException("Cannot update startup");
            if (Status.CanChange()) Status = enabled ? StartupStatus.Enabled : StartupStatus.Disabled;
            return Task.FromResult(Status);
        }
    }
}
