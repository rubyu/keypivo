using System.Diagnostics;

namespace Keypivo.Core;

public sealed record SwitchVerificationOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan StabilityPeriod { get; init; } = TimeSpan.FromSeconds(1);

    internal SwitchVerificationOptions Validate()
    {
        if (Timeout <= TimeSpan.Zero || Timeout.TotalMilliseconds > uint.MaxValue - 1
            || PollInterval <= TimeSpan.Zero || PollInterval > Timeout
            || StabilityPeriod < TimeSpan.Zero || StabilityPeriod >= Timeout)
            throw new ArgumentOutOfRangeException(nameof(SwitchVerificationOptions));
        return this;
    }
}

public sealed class SwitchVerificationTimeoutException(UsbDevice target, SwitchAction action) : UsbipdException(
    "The command finished, but the requested state could not be confirmed in time. " +
    "Status checks will continue automatically. Check that the same security key is still connected before trying again.")
{
    public UsbDevice Target { get; } = target;
    public SwitchAction Action { get; } = action;
}

internal sealed class StableDeviceState(TimeSpan stabilityPeriod)
{
    private long? _since;
    private ConnectionState? _state;

    public bool Observe(DeviceSnapshot snapshot, Func<UsbDevice, bool> matches)
    {
        if (snapshot.Error is not null || snapshot.Device is not { } device || !matches(device))
        {
            _since = null;
            _state = null;
            return false;
        }

        if (_since is null || _state != snapshot.State)
        {
            _since = Stopwatch.GetTimestamp();
            _state = snapshot.State;
        }
        return Stopwatch.GetElapsedTime(_since.Value) >= stabilityPeriod;
    }
}
