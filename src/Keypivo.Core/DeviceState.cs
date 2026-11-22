using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Keypivo.Core;

public enum ConnectionState
{
    Checking,
    ToolMissing,
    Error,
    NotConnected,
    WindowsUnshared,
    WindowsShared,
    ForcedShared,
    Attached
}

public sealed partial record UsbDevice
{
    [JsonRequired] public string InstanceId { get; init; } = "";
    [JsonRequired] public string? BusId { get; init; }
    [JsonRequired] public Guid? PersistedGuid { get; init; }
    [JsonRequired] public string? ClientIPAddress { get; init; }
    public string Description { get; init; } = "";
    public bool IsForced { get; init; }

    [JsonIgnore] public bool IsConnected => BusId is not null && BusIdPattern().IsMatch(BusId);
    [JsonIgnore] public string VendorId => HardwareIdPattern().Match(InstanceId ?? "").Groups[1].Value.ToUpperInvariant();
    [JsonIgnore] public bool IsShared => PersistedGuid.HasValue;
    [JsonIgnore] public bool IsAttached => !string.IsNullOrWhiteSpace(ClientIPAddress);
    [JsonIgnore] public string ProductId => HardwareIdPattern().Match(InstanceId ?? "").Groups[2].Value.ToUpperInvariant();

    [GeneratedRegex(@"\AUSB\\VID_([0-9A-F]{4})&PID_([0-9A-F]{4})(?:[&\\]|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HardwareIdPattern();

    [GeneratedRegex(@"\A[1-9][0-9]*-[1-9][0-9]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex BusIdPattern();
}

public sealed record DeviceSnapshot(ConnectionState State, UsbDevice? Device, int MatchingDeviceCount, string? Error = null)
{
    public static DeviceSnapshot Initial { get; } = new(ConnectionState.Checking, null, 0);
    public bool CanOperate => Device is not null;

    public static DeviceSnapshot FromDevices(IReadOnlyList<UsbDevice> devices, DeviceFilter? filter = null, UsbDevice? target = null)
    {
        // Keep usbipd's enumeration order; never select a disconnected persisted entry.
        filter ??= DeviceFilter.Default;
        var matches = devices.Where(d => d.IsConnected && filter.Matches(d)).ToArray();
        // A switch follows its original device even if enumeration order changes.
        var device = target is null ? matches.FirstOrDefault() : matches.FirstOrDefault(d =>
            string.Equals(d.InstanceId, target.InstanceId, StringComparison.OrdinalIgnoreCase) && d.BusId == target.BusId);
        var state = device switch
        {
            null => ConnectionState.NotConnected,
            { IsAttached: true } => ConnectionState.Attached,
            { IsForced: true } => ConnectionState.ForcedShared,
            { IsShared: true } => ConnectionState.WindowsShared,
            _ => ConnectionState.WindowsUnshared
        };
        return new(state, device, matches.Length);
    }
}

public static class UsbipdStateParser
{
    private sealed class StateDocument
    {
        [JsonRequired] public List<UsbDevice>? Devices { get; init; }
    }

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<UsbDevice> Parse(string json)
    {
        try
        {
            var state = JsonSerializer.Deserialize<StateDocument>(json.TrimStart('\uFEFF'), Options);
            if (state?.Devices is null || state.Devices.Any(d => d is null || string.IsNullOrWhiteSpace(d.InstanceId)))
                throw new JsonException("Missing Devices or InstanceId.");
            return state.Devices;
        }
        catch (JsonException exception)
        {
            throw new UsbipdException("Cannot read the device state. Check that usbipd-win 5.x or a compatible version is installed.", exception);
        }
    }
}
