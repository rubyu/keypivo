using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Keypivo.Core;

public sealed record CommandRequest(string FileName, IReadOnlyList<string> Arguments, TimeSpan Timeout);
public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);

public interface ICommandRunner
{
    Task<CommandResult> RunAsync(CommandRequest request, CancellationToken cancellationToken);
}

public sealed class ProcessCommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(request.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in request.Arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            process.Start();
        }
        catch (Win32Exception exception)
        {
            throw new UsbipdException($"Could not start usbipd (Windows error {exception.NativeErrorCode}).", exception);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);
        // Accessing these readers transfers disposal responsibility to us:
        // Process.Dispose leaves directly accessed streams open.
        using var outputReader = process.StandardOutput;
        using var errorReader = process.StandardError;
        var stdout = outputReader.ReadToEndAsync(timeout.Token);
        var stderr = errorReader.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return new(process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException) { }
            // Observe canceled output readers too, including when a child inherited a pipe handle.
            try { await Task.WhenAll(stdout, stderr); }
            catch (OperationCanceledException) { }
            if (cancellationToken.IsCancellationRequested) throw;
            throw new UsbipdException("usbipd timed out. Refresh the status before trying again.");
        }
    }
}
