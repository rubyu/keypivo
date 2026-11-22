using Keypivo.Core;

namespace Keypivo;

internal static class StatePresentation
{
    public static string Title(ConnectionState state) => state switch
    {
        ConnectionState.Checking => "Checking…",
        ConnectionState.ToolMissing => "Setup required",
        ConnectionState.Error => "Status unavailable",
        ConnectionState.NotConnected => "Not connected",
        ConnectionState.WindowsUnshared or ConnectionState.WindowsShared => "Available on Windows",
        ConnectionState.ForcedShared => "Shared · Waiting for connection",
        ConnectionState.Attached => "Connected to WSL",
        _ => "Unknown"
    };

    public static Color Color(ConnectionState state) => state switch
    {
        ConnectionState.WindowsUnshared or ConnectionState.WindowsShared => System.Drawing.Color.FromArgb(55, 101, 150),
        ConnectionState.Attached => System.Drawing.Color.FromArgb(21, 113, 92),
        ConnectionState.Error or ConnectionState.ToolMissing => System.Drawing.Color.FromArgb(169, 62, 40),
        ConnectionState.ForcedShared => System.Drawing.Color.FromArgb(166, 106, 10),
        _ => System.Drawing.Color.FromArgb(104, 113, 108)
    };
}
