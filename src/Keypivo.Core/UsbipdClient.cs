namespace Keypivo.Core;

public class UsbipdException(string message, Exception? innerException = null) : Exception(message, innerException);
public sealed class UsbipdNotFoundException() : UsbipdException(
    "usbipd-win was not found. Install it, then refresh the status.\r\n\r\nwinget install --interactive --exact dorssel.usbipd-win");

public enum UsbipdCommand { Attach, Detach }

public interface IUsbipdClient
{
    Task<IReadOnlyList<UsbDevice>> GetDevicesAsync(CancellationToken cancellationToken);
    Task ExecuteAsync(UsbipdCommand command, string busId, CancellationToken cancellationToken);
}

public sealed class UsbipdClient(ICommandRunner runner, Func<string>? locateExecutable = null) : IUsbipdClient
{
    private readonly Func<string> _locateExecutable = locateExecutable ?? LocateExecutable;

    public async Task<IReadOnlyList<UsbDevice>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(new(_locateExecutable(), ["state"], TimeSpan.FromSeconds(15)), cancellationToken);
        EnsureSuccess(result, "state");
        return UsbipdStateParser.Parse(result.StandardOutput);
    }

    public async Task ExecuteAsync(UsbipdCommand command, string busId, CancellationToken cancellationToken)
    {
        if (!new UsbDevice { BusId = busId }.IsConnected)
            throw new UsbipdException("The operation was stopped because the bus ID is invalid.");

        string[] arguments = command switch
        {
            UsbipdCommand.Attach => ["attach", "--wsl", "--busid", busId],
            UsbipdCommand.Detach => ["detach", "--busid", busId],
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
        var result = await runner.RunAsync(new(_locateExecutable(), arguments, TimeSpan.FromSeconds(60)), cancellationToken);
        EnsureSuccess(result, string.Join(' ', arguments));
    }

    private static void EnsureSuccess(CommandResult result, string command)
    {
        if (result.ExitCode == 0) return;
        var detail = string.Join(Environment.NewLine, new[] { result.StandardError.Trim(), result.StandardOutput.Trim() }
            .Where(s => s.Length > 0));
        if (detail.Length > 4000) detail = detail[..4000];
        if (detail.Length == 0)
            detail = "Check the usbipd service, administrator permissions, and device connection.";
        throw new UsbipdException($"usbipd {command} failed (exit code {result.ExitCode}).\r\n{detail}");
    }

    public static string LocateExecutable()
    {
        // Prefer the system installation, including after installation while this app is running.
        var roots = new[]
        {
            Environment.GetEnvironmentVariable("ProgramW6432"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };
        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            var path = Path.Combine(root!, "usbipd-win", "usbipd.exe");
            if (File.Exists(path)) return path;
        }
        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var directory = entry.Trim().Trim('"');
            // Do not execute an implicitly selected file from the working directory.
            if (!Path.IsPathFullyQualified(directory)) continue;
            var path = Path.Combine(directory, "usbipd.exe");
            if (File.Exists(path)) return Path.GetFullPath(path);
        }
        throw new UsbipdNotFoundException();
    }
}
