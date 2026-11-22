using Keypivo.Core;

namespace Keypivo.UiTests;

internal static class NotificationSettingsTests
{
    public static void Run()
    {
        AppSettings? saved = null;
        using (var form = Show(new(), settings => saved = settings))
        {
            var initialSize = form.Size;
            foreach (var category in new[] { "General", "Launch", "Connection", "Attention" })
            {
                Find<Button>(form, "Settings" + category).PerformClick();
                Application.DoEvents();
                Assert(form.Size == initialSize, "Category navigation must keep the window stable");
                Assert(Find<FlowLayoutPanel>(form, category + "Page").Visible, "Selected category must be visible");
                Program.CheckBounds(form);
                Program.Capture(form, "settings-" + category.ToLowerInvariant());
            }
            Find<CheckBox>(form, "NotifyActionFailed").Checked = false;
            Find<Button>(form, "SettingsConnection").PerformClick();
            var wsl = Find<CheckBox>(form, "NotifyConnectedToWsl");
            wsl.Checked = false;
            var master = Find<CheckBox>(form, "ShowNotificationsConnection");
            master.Checked = false;
            Assert(!wsl.Enabled && !wsl.Checked, "Master must disable controls without losing choices");
            Find<Button>(form, "SettingsAttention").PerformClick();
            Assert(!Find<CheckBox>(form, "ShowNotificationsAttention").Checked, "Master must agree across pages");
            Find<CheckBox>(form, "ShowNotificationsAttention").Checked = true;
            Assert(!Find<CheckBox>(form, "NotifyActionFailed").Checked && !wsl.Checked && wsl.Enabled,
                "Unmuting must preserve individual preferences across pages");
            Assert(saved is null, "Editing must not persist before Save");
            Find<Button>(form, "SaveSettings").PerformClick();
            Assert(saved?.Notifications == new NotificationSettings { ConnectedToWsl = false, ActionFailed = false },
                "Save must include changes from all categories");
        }
        using (var form = Show(saved!, _ => throw new Exception("Cancel must not apply settings")))
        {
            Find<Button>(form, "SettingsConnection").PerformClick();
            var toggle = Find<CheckBox>(form, "NotifyConnectedToWsl");
            Assert(!toggle.Checked, "Reopening must show saved preferences");
            toggle.Checked = true;
            Find<Button>(form, "CancelSettings").PerformClick();
        }
        using (var form = Show(new(), settings => saved = settings))
        {
            var vid = form.Controls.Find("GeneralPage", true).Single().Controls.OfType<TextBox>().First();
            vid.Text = "bad";
            Find<Button>(form, "SettingsAttention").PerformClick();
            Find<Button>(form, "SaveSettings").PerformClick();
            Assert(form.Visible && Find<FlowLayoutPanel>(form, "GeneralPage").Visible && vid.Focused,
                "Validation must return to and focus the invalid general field");
            Program.CheckBounds(form);
        }
        using (var form = Show(new(), _ => { }))
        {
            Find<Button>(form, "SettingsConnection").PerformClick();
            var toggle = Find<CheckBox>(form, "NotifyConnectedToWsl");
            Assert(toggle.AccessibilityObject.Role == AccessibleRole.CheckButton, "Switch must expose native checkbox semantics");
            Assert(toggle.AccessibilityObject.State.HasFlag(AccessibleStates.Checked), "Assistive technology must read the on state");
            toggle.AccessibilityObject.DoDefaultAction();
            Assert(!toggle.Checked, "Accessible default action must toggle the setting");
        }
        Console.WriteLine("PASS Native notification settings: categories, bounds, master, individual choices, Save/Cancel, validation, accessibility.");
    }

    private static SettingsForm Show(AppSettings settings, Action<AppSettings> apply)
    {
        var form = new SettingsForm(settings, true, false, value => { apply(value); return Task.CompletedTask; }, new Startup());
        form.Show();
        Application.DoEvents();
        return form;
    }

    private sealed class Startup : IStartupService
    {
        public Task<StartupStatus> GetStatusAsync() => Task.FromResult(StartupStatus.Enabled);
        public Task<StartupStatus> SetEnabledAsync(bool enabled) => Task.FromResult(enabled ? StartupStatus.Enabled : StartupStatus.Disabled);
    }

    private static T Find<T>(Control parent, string name) where T : Control => parent.Controls.Find(name, true).OfType<T>().Single();
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
