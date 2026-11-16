using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Keypivo.Core;

public sealed record CommandRequest(string FileName, IReadOnlyList<string> Arguments, bool Elevated, TimeSpan Timeout);
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
            UseShellExecute = request.Elevated,
            CreateNoWindow = !request.Elevated,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = !request.Elevated,
            RedirectStandardError = !request.Elevated
        };
        if (request.Elevated) startInfo.Verb = "runas";
        else
        {
            startInfo.StandardOutputEncoding = Encoding.UTF8;
            startInfo.StandardErrorEncoding = Encoding.UTF8;
        }
        foreach (var argument in request.Arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // ShellExecute may wait for UAC. Never block the WinForms message loop.
            if (request.Elevated) await Task.Run(() => process.Start(), cancellationToken);
            else process.Start();
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new UsbipdException("The administrator permission request was canceled.", exception);
        }
        catch (Win32Exception exception)
        {
            throw new UsbipdException($"Could not start usbipd (Windows error {exception.NativeErrorCode}).", exception);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);
        var stdout = request.Elevated ? Task.FromResult("") : process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = request.Elevated ? Task.FromResult("") : process.StandardError.ReadToEndAsync(timeout.Token);
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
            throw new UsbipdException("usbipd timed out. Refresh the status before trying again. An elevated operation may still be running.");
        }
    }
}
