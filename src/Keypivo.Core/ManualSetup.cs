namespace Keypivo.Core;

public enum SetupKind { Install, Share, Unshare }

public sealed record SetupCheckResult(bool IsComplete, bool CanCopy, string? Message);

public sealed class ManualSetup
{
    public const string InstallCommand = "winget install --interactive --exact dorssel.usbipd-win";
    public const string InstallationHelpUrl = "https://learn.microsoft.com/en-us/windows/wsl/connect-usb#install-the-usbipd-win-project";

    public SetupKind Kind { get; }
    public UsbDevice? Target { get; }
    public string Command { get; }
    public string Title => Kind switch
    {
        SetupKind.Install => "Install usbipd-win",
        SetupKind.Share => "Set up your key",
        _ => "Stop sharing this key"
    };
    public string Subtitle => Kind switch
    {
        SetupKind.Install => "Required to use your key in WSL.",
        SetupKind.Share => "Set up this key once to use it in WSL.",
        _ => "Set up sharing again to use this key in WSL."
    };
    public string CommandLabel => Kind == SetupKind.Install ? "Open PowerShell and run:" : "Run in administrator PowerShell";
    public string CheckLabel => Kind == SetupKind.Install ? "Check installation" : "Check status";

    public ManualSetup(SetupKind kind, UsbDevice? target = null)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind != SetupKind.Install && (target is null || !target.IsConnected || string.IsNullOrWhiteSpace(target.VendorId)))
            throw new ArgumentException("A connected USB device is required.", nameof(target));
        Kind = kind;
        Target = kind == SetupKind.Install ? null : target;
        Command = kind == SetupKind.Install ? InstallCommand : $"usbipd {(kind == SetupKind.Share ? "bind" : "unbind")} --busid {target!.BusId}";
    }

    public SetupCheckResult Inspect(DeviceSnapshot snapshot)
    {
        if (snapshot.State == ConnectionState.ToolMissing)
            return new(false, Kind == SetupKind.Install, "usbipd-win was not found. Finish installation, then try again.");
        if (snapshot.State is ConnectionState.Error or ConnectionState.Checking)
            return new(false, Kind == SetupKind.Install, "Unable to read device status. Check usbipd-win and try again.");
        // A successful state query confirms installation even when no key is connected.
        if (Kind == SetupKind.Install) return new(true, true, null);
        var device = snapshot.Device;
        if (device is null || !device.IsConnected ||
            !string.Equals(device.InstanceId, Target!.InstanceId, StringComparison.OrdinalIgnoreCase) || device.BusId != Target.BusId)
            return new(false, false, "Reconnect the same security key, then check again.");
        if (Kind == SetupKind.Unshare && device.IsAttached)
            return new(false, false, "Switch to Windows before stopping sharing.");
        var complete = Kind == SetupKind.Share ? device.IsShared : !device.IsShared && !device.IsForced;
        return new(complete, true, complete ? null : Kind == SetupKind.Share
            ? "Sharing is not enabled yet. Run the command, then check again."
            : "This key is still shared. Run the command, then check again.");
    }
}

public sealed class ManualSetupRequiredException(ManualSetup setup) : UsbipdException("Manual setup is required.")
{
    public ManualSetup Setup { get; } = setup;
}
