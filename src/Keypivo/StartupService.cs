using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;

namespace Keypivo;

internal enum StartupStatus { Enabled, Disabled, DisabledByUser, DisabledByPolicy, EnabledByPolicy, Unpackaged, Unavailable }

internal static class StartupPresentation
{
    public static bool IsEnabled(this StartupStatus status) => status is StartupStatus.Enabled or StartupStatus.EnabledByPolicy;
    public static bool CanChange(this StartupStatus status) => status is StartupStatus.Enabled or StartupStatus.Disabled;
    public static string? Hint(this StartupStatus status) => status switch
    {
        StartupStatus.DisabledByUser => "Turn on Keypivo in Windows startup settings.",
        StartupStatus.DisabledByPolicy or StartupStatus.EnabledByPolicy => "Managed by your organization.",
        StartupStatus.Unpackaged => "Available in the Microsoft Store version.",
        StartupStatus.Unavailable => "Startup settings are unavailable. Reopen Settings to try again.",
        _ => null
    };
}

internal interface IStartupService
{
    Task<StartupStatus> GetStatusAsync();
    Task<StartupStatus> SetEnabledAsync(bool enabled);
}

internal sealed class StartupService : IStartupService
{
    internal const string TaskId = "KeypivoStartup";

    public async Task<StartupStatus> GetStatusAsync()
    {
        if (!HasPackageIdentity()) return StartupStatus.Unpackaged;
        return Map((await StartupTask.GetAsync(TaskId)).State);
    }

    public async Task<StartupStatus> SetEnabledAsync(bool enabled)
    {
        if (!HasPackageIdentity()) return StartupStatus.Unpackaged;
        var task = await StartupTask.GetAsync(TaskId);
        var current = Map(task.State);
        // Windows owns this preference. Never override a user/policy decision,
        // including one made after Keypivo's Settings window was opened.
        if (!current.CanChange() || current.IsEnabled() == enabled) return current;
        if (enabled) return Map(await task.RequestEnableAsync());
        task.Disable();
        return Map((await StartupTask.GetAsync(TaskId)).State);
    }

    internal static StartupStatus Map(StartupTaskState state) => state switch
    {
        StartupTaskState.Enabled => StartupStatus.Enabled,
        StartupTaskState.Disabled => StartupStatus.Disabled,
        StartupTaskState.DisabledByUser => StartupStatus.DisabledByUser,
        StartupTaskState.DisabledByPolicy => StartupStatus.DisabledByPolicy,
        StartupTaskState.EnabledByPolicy => StartupStatus.EnabledByPolicy,
        _ => StartupStatus.Unavailable
    };

    private static bool HasPackageIdentity()
    {
        uint length = 0;
        var result = GetCurrentPackageFullName(ref length, IntPtr.Zero);
        return result switch
        {
            122 => true, // ERROR_INSUFFICIENT_BUFFER: identity exists; only its presence is needed.
            15700 => false, // APPMODEL_ERROR_NO_PACKAGE
            _ => throw new Win32Exception(result)
        };
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);
}
