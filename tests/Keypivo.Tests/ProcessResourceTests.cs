using System.Diagnostics;
using Keypivo.Core;

internal static class ProcessResourceTests
{
    public static async Task RunAsync()
    {
        if (!OperatingSystem.IsWindows()) return;
        var runner = new ProcessCommandRunner();
        var command = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var quick = new CommandRequest(command, ["/d", "/c", "exit 0"], TimeSpan.FromSeconds(10));
        // Warm up process startup, async waits, and handle-count instrumentation.
        using var current = Process.GetCurrentProcess();
        for (var i = 0; i < 8; i++) await runner.RunAsync(quick, default);
        _ = current.HandleCount;

        foreach (var mode in new[] { "success", "timeout", "cancellation" })
        {
            var count = mode == "success" ? 64 : 16;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (!GC.TryStartNoGCRegion(64 * 1024 * 1024))
                throw new Exception("Cannot reserve the resource-lifetime test's no-GC region.");
            try
            {
                current.Refresh();
                var before = current.HandleCount;
                for (var i = 0; i < count; i++)
                {
                    if (mode == "success")
                    {
                        await runner.RunAsync(quick, default);
                        continue;
                    }
                    var slow = new CommandRequest(command, ["/d", "/c", "ping -n 5 127.0.0.1 >nul"],
                        mode == "timeout" ? TimeSpan.FromMilliseconds(200) : TimeSpan.FromSeconds(10));
                    using var cancellation = new CancellationTokenSource();
                    if (mode == "cancellation") cancellation.CancelAfter(200);
                    try
                    {
                        await runner.RunAsync(slow, cancellation.Token);
                        throw new Exception($"The {mode} command unexpectedly completed.");
                    }
                    catch (UsbipdException) when (mode == "timeout") { }
                    catch (OperationCanceledException) when (mode == "cancellation" && cancellation.IsCancellationRequested) { }
                }
                await Task.Delay(50);
                current.Refresh();
                var growth = current.HandleCount - before;
                Console.WriteLine($"PROCESS_HANDLES mode={mode} iterations={count} growthBeforeGC={growth}");
                // The runtime's async process wait can defer one handle per run.
                // Two additional pipe handles per run must NOT await finalization.
                if (growth > count + 20)
                    throw new Exception($"Command streams retain handles until GC: {mode}, growth={growth}, iterations={count}.");
            }
            finally { GC.EndNoGCRegion(); }
        }
    }
}
