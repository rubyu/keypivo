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
    private string _instanceId = "";
    private string _vendorId = "";
    private string _productId = "";

    [JsonRequired]
    public string InstanceId
    {
        get => _instanceId;
        init
        {
            _instanceId = value;
            _vendorId = _productId = "";
            var id = value.AsSpan();
            if (id.Length < 21 || !id.StartsWith(@"USB\VID_", StringComparison.OrdinalIgnoreCase)
                || !id.Slice(12, 5).Equals("&PID_", StringComparison.OrdinalIgnoreCase)
                || id.Slice(8, 4).IndexOfAnyExcept("0123456789abcdefABCDEF") >= 0
                || id.Slice(17, 4).IndexOfAnyExcept("0123456789abcdefABCDEF") >= 0
                || (id.Length > 21 && id[21] is not ('&' or '\\'))) return;
            _vendorId = id.Slice(8, 4).ToString().ToUpperInvariant();
            _productId = id.Slice(17, 4).ToString().ToUpperInvariant();
        }
    }
    [JsonRequired] public string? BusId { get; init; }
    [JsonRequired] public Guid? PersistedGuid { get; init; }
    [JsonRequired] public string? ClientIPAddress { get; init; }
    public string Description { get; init; } = "";
    public bool IsForced { get; init; }

    [JsonIgnore] public bool IsConnected => BusId is not null && BusIdPattern().IsMatch(BusId);
    [JsonIgnore] public string VendorId => _vendorId;
    [JsonIgnore] public bool IsShared => PersistedGuid.HasValue;
    [JsonIgnore] public bool IsAttached => !string.IsNullOrWhiteSpace(ClientIPAddress);
    [JsonIgnore] public string ProductId => _productId;

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
        UsbDevice? device = null;
        var matchingCount = 0;
        for (var i = 0; i < devices.Count; i++)
        {
            var candidate = devices[i];
            if (!candidate.IsConnected || !filter.Matches(candidate)) continue;
            matchingCount++;
            // A switch follows its original device even if enumeration order changes.
            if (device is null && (target is null ||
                (string.Equals(candidate.InstanceId, target.InstanceId, StringComparison.OrdinalIgnoreCase)
                    && candidate.BusId == target.BusId))) device = candidate;
        }
        var state = device switch
        {
            null => ConnectionState.NotConnected,
            { IsAttached: true } => ConnectionState.Attached,
            { IsForced: true } => ConnectionState.ForcedShared,
            { IsShared: true } => ConnectionState.WindowsShared,
            _ => ConnectionState.WindowsUnshared
        };
        return new(state, device, matchingCount);
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
