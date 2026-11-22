using Keypivo.Core;
using System.Globalization;

namespace Keypivo;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        // Keep the existing identity so an older release cannot control USB alongside this app.
        using var mutex = new Mutex(true, @"Local\YSwitch.Tray.1050", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("Keypivo is already running. Click its icon in the system tray.",
                "Keypivo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var store = SettingsStore.CreateDefault();
            var loaded = store.Load();
            var controller = new SwitchController(new UsbipdClient(new ProcessCommandRunner()), filter: loaded.Settings.ToFilter());
            using var context = new TrayApplicationContext(controller, store, loaded,
                args.Contains("--show", StringComparer.OrdinalIgnoreCase));
            Application.Run(context);
        }
        finally { mutex.ReleaseMutex(); }
    }
}
