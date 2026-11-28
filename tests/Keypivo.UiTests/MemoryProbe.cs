using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Keypivo.Core;

namespace Keypivo.UiTests;

// Opt-in measurement of the real tray context with a read-only synthetic USB
// response. Neither variant calls usbipd or changes the user's devices/settings.
internal static class MemoryProbe
{
    public static int Run(string[] args)
    {
        if (args.Length != 3 || args[1] is not ("baseline" or "fixed"))
            throw new ArgumentException("Usage: --memory-probe baseline|fixed output.json");
        using var dispatcher = new Control();
        _ = dispatcher.Handle;
        var exitCode = 1;
        dispatcher.BeginInvoke((Action)(async () =>
        {
            try { await MeasureAsync(args[1], args[2]); exitCode = 0; }
            catch (Exception error) { Console.Error.WriteLine(error); }
            finally { Application.ExitThread(); }
        }));
        Application.Run();
        return exitCode;
    }

    private static async Task MeasureAsync(string variant, string output)
    {
        var directory = Path.Combine(Path.GetTempPath(), "keypivo-memory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var fixture = Path.Combine(directory, "state.json");
        var devices = Enumerable.Range(1, 16).Select(i => new UsbDevice
        {
            InstanceId = $@"USB\VID_{(i == 1 ? "1050" : "1234")}&PID_0407\sample-{i}",
            BusId = $"4-{i}", Description = "USB Input Device, Microsoft Usbccid Smartcard Reader (WUDF)",
            PersistedGuid = Guid.Parse("6f9619ff-8b86-4b80-b42d-00c04fc964ff")
        }).ToArray();
        File.WriteAllText(fixture, JsonSerializer.Serialize(new { Devices = devices }));
        ICommandRunner runner = variant == "baseline" ? new BaselineProcessCommandRunner() : new ProcessCommandRunner();
        var adapter = new FixtureRunner(runner, fixture);
        using var context = new TrayApplicationContext(
            new SwitchController(new UsbipdClient(adapter, () => "memory-probe")),
            new SettingsStore(Path.Combine(directory, "settings.json")),
            new(new() { Notifications = new() { Enabled = false } }, false), showWindow: false);
        var timer = (System.Windows.Forms.Timer)typeof(TrayApplicationContext)
            .GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(context)!;
        while (!timer.Enabled) await Task.Delay(10);
        timer.Stop();
        for (var i = 0; i < 16; i++) await context.RefreshAsync();

        using var process = Process.GetCurrentProcess();
        var samples = new List<Sample>();
        var clock = Stopwatch.StartNew();
        void Take(string phase)
        {
            process.Refresh();
            var memory = new MemoryCounters { Size = (uint)Marshal.SizeOf<MemoryCounters>() };
            if (!GetProcessMemoryInfo(process.Handle, ref memory, memory.Size))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var gc = GC.GetGCMemoryInfo();
            var sample = new Sample(phase, clock.Elapsed.TotalSeconds, adapter.Reads,
                (long)memory.PrivateWorkingSet, (long)memory.PrivateUsage, (long)memory.WorkingSet,
                GC.GetTotalMemory(false), gc.TotalCommittedBytes, gc.HeapSizeBytes,
                process.HandleCount, GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
            samples.Add(sample);
            Console.WriteLine("MEMORY_SAMPLE " + variant + " " + JsonSerializer.Serialize(sample));
        }

        // Collection is used only at measurement boundaries in these isolated
        // probe processes. The application does not acquire a forced-GC policy.
        Collect();
        Take("start");
        for (var i = 1; i <= 1200; i++)
        {
            await context.RefreshAsync();
            if (i % 200 == 0) Take("stress-" + i);
        }
        timer.Start();
        for (var i = 1; i <= 12; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            Take("idle-" + i);
        }
        timer.Stop();
        // Join any in-flight read before measuring.
        await context.RefreshAsync();
        Collect();
        Take("after-gc");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            Variant = variant, Runtime = RuntimeInformation.FrameworkDescription,
            Framework = typeof(Form).Assembly.GetName().Version?.ToString(),
            DeviceCount = devices.Length, StressReads = 1200, IdleSeconds = 120,
            Baseline = "v1.0.59.0 ProcessCommandRunner", Samples = samples
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class FixtureRunner(ICommandRunner runner, string fixture) : ICommandRunner
    {
        public int Reads { get; private set; }
        public async Task<CommandResult> RunAsync(CommandRequest request, CancellationToken cancellationToken)
        {
            if (!request.Arguments.SequenceEqual(new[] { "state" }))
                throw new InvalidOperationException("The memory probe permits state reads only.");
            var result = await runner.RunAsync(new(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                ["/d", "/c", "type", fixture], request.Timeout), cancellationToken);
            Reads++;
            return result;
        }
    }

    private sealed record Sample(string Phase, double Seconds, int Reads, long PrivateWorkingSet,
        long PrivateCommit, long WorkingSet, long ManagedHeap, long GcCommitted, long HeapAfterLastGc,
        int Handles, int Gen0, int Gen1, int Gen2);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryCounters
    {
        public uint Size, PageFaultCount;
        public nuint PeakWorkingSet, WorkingSet, QuotaPeakPagedPool, QuotaPagedPool,
            QuotaPeakNonPagedPool, QuotaNonPagedPool, PagefileUsage, PeakPagefileUsage,
            PrivateUsage, PrivateWorkingSet;
        public ulong SharedCommit;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(IntPtr process, ref MemoryCounters counters, uint size);
}
