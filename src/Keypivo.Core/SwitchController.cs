namespace Keypivo.Core;

public enum SwitchAction { Attach, Detach, Toggle, Unshare }

public sealed class SwitchController(IUsbipdClient client, DeviceFilter? filter = null, SwitchVerificationOptions? verification = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SwitchVerificationOptions _verification = (verification ?? new()).Validate();
    private PendingConfirmation? _pending;
    public DeviceFilter Filter { get; private set; } = filter ?? DeviceFilter.Default;
    public DeviceSnapshot Current { get; private set; } = DeviceSnapshot.Initial;
    public SwitchVerificationTimeoutException? PendingVerification => _pending?.Timeout;
    public bool IsConfirmingCurrentState => _pending is { } pending && Current.Device is { } device
        && string.Equals(device.InstanceId, pending.Timeout.Target.InstanceId, StringComparison.OrdinalIgnoreCase)
        && device.BusId == pending.Timeout.Target.BusId && MatchesAction(device, pending.Timeout.Action);
    public event Action<DeviceSnapshot>? SnapshotChanged;
    public event Action<SwitchVerificationTimeoutException, DeviceSnapshot>? VerificationRecovered;

    private sealed record PendingConfirmation(SwitchVerificationTimeoutException Timeout, StableDeviceState Stability);

    public async Task UpdateFilterAsync(DeviceFilter filter, CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
            throw new UsbipdException("Another operation is in progress. Try again when it finishes.");
        try
        {
            Filter = filter;
            _pending = null;
            Current = DeviceSnapshot.Initial;
            SnapshotChanged?.Invoke(Current);
            await ReadAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken)) return;
        try { await ReadAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task ExecuteAsync(SwitchAction action, CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
            throw new UsbipdException("Another operation is in progress. Try again when it finishes.");
        try
        {
            _pending = null;
            // The tray can be stale. Always select and decide from a fresh snapshot.
            var initial = await ReadAsync(cancellationToken);
            var target = initial.Device ?? throw new UsbipdException(initial.Error ?? "No security key matches the current VID and PID settings.");
            if (action == SwitchAction.Toggle)
                action = target.IsAttached ? SwitchAction.Detach : SwitchAction.Attach;

            switch (action)
            {
                case SwitchAction.Attach:
                    if (target.IsAttached) break;
                    if (!target.IsShared)
                    {
                        await client.ExecuteAsync(UsbipdCommand.Bind, target.BusId!, cancellationToken);
                        target = await VerifyAsync(target, d => d.IsShared, action, cancellationToken);
                    }
                    if (!target.IsAttached)
                        await client.ExecuteAsync(UsbipdCommand.Attach, target.BusId!, cancellationToken);
                    await VerifyAsync(target, d => d.IsAttached, action, cancellationToken);
                    break;
                case SwitchAction.Detach:
                case SwitchAction.Unshare:
                    if (target.IsAttached)
                    {
                        await client.ExecuteAsync(UsbipdCommand.Detach, target.BusId!, cancellationToken);
                        target = await VerifyAsync(target,
                            d => !d.IsAttached && (action == SwitchAction.Unshare || !d.IsForced), action, cancellationToken);
                    }
                    if (action == SwitchAction.Unshare && (target.IsShared || target.IsForced))
                    {
                        await client.ExecuteAsync(UsbipdCommand.Unbind, target.BusId!, cancellationToken);
                        await VerifyAsync(target, d => MatchesAction(d, action), action, cancellationToken);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SwitchVerificationTimeoutException)
        {
            // A failed command can still have changed USB state. Never guess the result.
            if (!cancellationToken.IsCancellationRequested) await ReadAsync(cancellationToken);
            throw;
        }
        finally { _gate.Release(); }
    }

    private async Task<UsbDevice> VerifyAsync(UsbDevice target, Func<UsbDevice, bool> predicate,
        SwitchAction action, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_verification.Timeout);
        var stability = new StableDeviceState(_verification.StabilityPeriod);
        DeviceSnapshot? latest = null;
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                (_, latest) = await ReadStateAsync(deadline.Token, target);
                deadline.Token.ThrowIfCancellationRequested();
                if (latest.Error is not null) throw new UsbipdException(latest.Error);
                if (stability.Observe(latest, predicate))
                {
                    Publish(latest);
                    return latest.Device!;
                }
                // USB re-enumeration can temporarily remove the device or leave a disconnected registration.
                // Keep the popup in Switching; never publish these intermediate observations as transitions.
                await Task.Delay(_verification.PollInterval, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            var timeout = new SwitchVerificationTimeoutException(target, action);
            _pending = new(timeout, new(_verification.StabilityPeriod));
            if (latest is not null) Publish(latest);
            throw timeout;
        }
    }

    private static bool MatchesAction(UsbDevice device, SwitchAction action) => action switch
    {
        SwitchAction.Attach => device.IsAttached,
        SwitchAction.Detach => !device.IsAttached && !device.IsForced,
        SwitchAction.Unshare => !device.IsShared && !device.IsAttached && !device.IsForced,
        _ => false
    };

    private async Task<DeviceSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var (devices, snapshot) = await ReadStateAsync(cancellationToken);
        Current = snapshot;
        if (_pending is { } pending)
        {
            var targetSnapshot = snapshot.Error is not null ? snapshot : DeviceSnapshot.FromDevices(devices, Filter, pending.Timeout.Target);
            if (pending.Stability.Observe(targetSnapshot, d => MatchesAction(d, pending.Timeout.Action)))
            {
                _pending = null;
                VerificationRecovered?.Invoke(pending.Timeout, targetSnapshot);
            }
        }
        SnapshotChanged?.Invoke(snapshot);
        return snapshot;
    }

    private async Task<(IReadOnlyList<UsbDevice> Devices, DeviceSnapshot Snapshot)> ReadStateAsync(
        CancellationToken cancellationToken, UsbDevice? target = null)
    {
        IReadOnlyList<UsbDevice> devices = [];
        DeviceSnapshot snapshot;
        try
        {
            devices = await client.GetDevicesAsync(cancellationToken);
            snapshot = DeviceSnapshot.FromDevices(devices, Filter, target);
        }
        catch (UsbipdNotFoundException exception) { snapshot = new(ConnectionState.ToolMissing, null, 0, exception.Message); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            snapshot = new(ConnectionState.Error, null, 0, exception.Message);
        }
        return (devices, snapshot);
    }

    private void Publish(DeviceSnapshot snapshot)
    {
        Current = snapshot;
        SnapshotChanged?.Invoke(snapshot);
    }
}
