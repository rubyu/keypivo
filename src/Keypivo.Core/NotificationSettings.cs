namespace Keypivo.Core;

public enum NotificationKind
{
    CurrentStatusAtLaunch, AvailableOnWindows, ConnectedToWsl, KeyDisconnected, WaitingForConnection,
    SetupRequired, StatusUnavailable, ActionFailed, ChangeNotConfirmed, SettingsCouldNotBeLoaded
}

public sealed record NotificationSettings
{
    public bool Enabled { get; init; } = true;
    public bool CurrentStatusAtLaunch { get; init; } = true;
    public bool AvailableOnWindows { get; init; } = true;
    public bool ConnectedToWsl { get; init; } = true;
    public bool KeyDisconnected { get; init; } = true;
    public bool WaitingForConnection { get; init; } = true;
    public bool SetupRequired { get; init; } = true;
    public bool StatusUnavailable { get; init; } = true;
    public bool ActionFailed { get; init; } = true;
    public bool ChangeNotConfirmed { get; init; } = true;
    public bool SettingsCouldNotBeLoaded { get; init; } = true;

    public bool Allows(NotificationKind kind) => Enabled && IsSelected(kind);

    public bool IsSelected(NotificationKind kind) => kind switch
    {
        NotificationKind.CurrentStatusAtLaunch => CurrentStatusAtLaunch,
        NotificationKind.AvailableOnWindows => AvailableOnWindows,
        NotificationKind.ConnectedToWsl => ConnectedToWsl,
        NotificationKind.KeyDisconnected => KeyDisconnected,
        NotificationKind.WaitingForConnection => WaitingForConnection,
        NotificationKind.SetupRequired => SetupRequired,
        NotificationKind.StatusUnavailable => StatusUnavailable,
        NotificationKind.ActionFailed => ActionFailed,
        NotificationKind.ChangeNotConfirmed => ChangeNotConfirmed,
        NotificationKind.SettingsCouldNotBeLoaded => SettingsCouldNotBeLoaded,
        _ => false
    };

    public NotificationSettings WithSelection(NotificationKind kind, bool enabled) => kind switch
    {
        NotificationKind.CurrentStatusAtLaunch => this with { CurrentStatusAtLaunch = enabled },
        NotificationKind.AvailableOnWindows => this with { AvailableOnWindows = enabled },
        NotificationKind.ConnectedToWsl => this with { ConnectedToWsl = enabled },
        NotificationKind.KeyDisconnected => this with { KeyDisconnected = enabled },
        NotificationKind.WaitingForConnection => this with { WaitingForConnection = enabled },
        NotificationKind.SetupRequired => this with { SetupRequired = enabled },
        NotificationKind.StatusUnavailable => this with { StatusUnavailable = enabled },
        NotificationKind.ActionFailed => this with { ActionFailed = enabled },
        NotificationKind.ChangeNotConfirmed => this with { ChangeNotConfirmed = enabled },
        NotificationKind.SettingsCouldNotBeLoaded => this with { SettingsCouldNotBeLoaded = enabled },
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static NotificationKind? ForState(ConnectionState state) => state switch
    {
        ConnectionState.WindowsShared or ConnectionState.WindowsUnshared => NotificationKind.AvailableOnWindows,
        ConnectionState.Attached => NotificationKind.ConnectedToWsl,
        ConnectionState.NotConnected => NotificationKind.KeyDisconnected,
        ConnectionState.ForcedShared => NotificationKind.WaitingForConnection,
        ConnectionState.ToolMissing => NotificationKind.SetupRequired,
        ConnectionState.Error => NotificationKind.StatusUnavailable,
        _ => null
    };
}
