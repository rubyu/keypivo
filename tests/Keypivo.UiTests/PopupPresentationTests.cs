using System.Runtime.InteropServices;
using Keypivo.Core;

namespace Keypivo.UiTests;

internal static class PopupPresentationTests
{
    public static void Run()
    {
        using var other = new Form { Text = "Popup presentation test", TopMost = true };
        using var popup = new StatusForm { AllowClose = true };
        other.Show();
        other.Activate();
        Application.DoEvents();

        // Repeat to cover both the initial opening and reopening after dismissal.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            popup.ShowAt(DisplayPosition.Center);
            Application.DoEvents();
            Assert(popup.Visible && popup.TopMost, "Opening the popup must raise it above ordinary windows");
            AssertAbove(popup, other);
            Assert(GetForegroundWindow() == popup.Handle, "Opening the popup must give it foreground focus");
            other.Activate();
            Application.DoEvents();
            Assert(!popup.Visible && !popup.TopMost, "Leaving the menu must hide it and release topmost status");
        }

        other.TopMost = false;
        popup.ShowSetup(new(SetupKind.Install));
        popup.ShowAt(DisplayPosition.Center);
        Application.DoEvents();
        other.Activate();
        Application.DoEvents();
        Assert(popup.Visible && popup.IsSetupVisible && !popup.TopMost,
            "Instructions must stay open without covering the user's other window");

        popup.RenderSetup(new(false, true, null), null, false);
        Application.DoEvents();
        Assert(GetForegroundWindow() == other.Handle && !popup.TopMost,
            "Background status checks must not steal focus or raise instructions");
        popup.ShowAt(DisplayPosition.Center);
        Application.DoEvents();
        Assert(popup.TopMost && GetForegroundWindow() == popup.Handle, "Reopening visible instructions must bring them forward");
        popup.Hide();
        Assert(!popup.TopMost, "Explicit hiding must release topmost status too");

        popup.ShowMenu();
        popup.KeepOpen = true;
        popup.ShowAt(DisplayPosition.Center);
        using var settings = new SettingsForm(new(), false, false, _ => Task.CompletedTask);
        settings.Show(popup);
        Application.DoEvents();
        Assert(popup.Visible && !popup.TopMost && GetForegroundWindow() == settings.Handle,
            "Opening Settings must keep the owner visible without pinning it in front of the dialog");
        settings.Close();
        popup.Close();
        other.Close();
        Console.WriteLine("PASS Native popup: foreground, topmost ordering, reopen, dismissal, background instructions, and Settings.");
    }

    private static void AssertAbove(Form popup, Form other)
    {
        // Walk the actual native z-order; a TopMost property assertion alone would
        // not show whether the popup is still below an existing topmost window.
        var window = GetWindow(popup.Handle, 0); // GW_HWNDFIRST
        for (var count = 0; window != IntPtr.Zero && count < 1000; count++, window = GetWindow(window, 2)) // GW_HWNDNEXT
        {
            if (window == popup.Handle) return;
            if (window == other.Handle) throw new Exception("Popup is behind the existing topmost window");
        }
        throw new Exception("Popup was not found in the native z-order");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);
}
