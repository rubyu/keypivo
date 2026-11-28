using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Keypivo.Core;

namespace Keypivo.UiTests;

// Diagnostic-only: fresh tray process, synthetic devices, no USB commands and
// no collection before/during the workload. The final GC distinguishes live
// objects from garbage and reusable heap capacity; it is not an app policy.
internal static class MemoryGrowthProbe
{
    public static int Run(string[] args)
    {
        if (args.Length is not (3 or 5) || args[1] is not ("idle" or "json" or "process" or "missing"))
            throw new ArgumentException("Usage: --memory-growth idle|json|process|missing output.json [normalSeconds acceleratedReads]");
        var normalSeconds = args.Length == 5 ? int.Parse(args[3]) : 120;
        var acceleratedReads = args.Length == 5 ? int.Parse(args[4]) : 2000;
        if (normalSeconds is < 10 or > 600 || normalSeconds % 10 != 0 || acceleratedReads is < 200 or > 10000)
            throw new ArgumentException("Invalid diagnostic workload duration or read count.");
        using var dispatcher = new Control();
        _ = dispatcher.Handle;
        var exitCode = 1;
        dispatcher.BeginInvoke((Action)(async () =>
        {
            try { await MeasureAsync(args[1], args[2], normalSeconds, acceleratedReads); exitCode = 0; }
            catch (Exception error) { Console.Error.WriteLine(error); }
            finally { Application.ExitThread(); }
        }));
        Application.Run();
        return exitCode;
    }

    private static async Task MeasureAsync(string variant, string output, int normalSeconds, int acceleratedReads)
    {
        var directory = Path.Combine(Path.GetTempPath(), "keypivo-growth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var fixture = Path.Combine(directory, "state.json");
        var devices = Enumerable.Range(1, 16).Select(i => new UsbDevice
        {
            InstanceId = $@"USB\VID_{(i == 1 && variant != "missing" ? "1050" : "1234")}&PID_0407\sample-{i}",
            BusId = $"4-{i}", Description = "USB Input Device, Microsoft Usbccid Smartcard Reader (WUDF)",
            PersistedGuid = Guid.Parse("6f9619ff-8b86-4b80-b42d-00c04fc964ff")
        }).ToArray();
        var json = JsonSerializer.Serialize(new { Devices = devices });
        File.WriteAllText(fixture, json);
        var runner = new FixtureRunner(json, fixture, variant != "json");
        var controller = new SwitchController(new UsbipdClient(runner, () => "memory-growth"));
        using var process = Process.GetCurrentProcess();
        var samples = new List<Sample>(50);
        var latencies = new double[acceleratedReads];
        var clock = Stopwatch.StartNew();
        void Take(string phase)
        {
            process.Refresh();
            var memory = new MemoryCounters { Size = (uint)Marshal.SizeOf<MemoryCounters>() };
            if (!GetProcessMemoryInfo(process.Handle, ref memory, memory.Size))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var gc = GC.GetGCMemoryInfo();
            samples.Add(new(phase, clock.Elapsed.TotalSeconds, runner.Reads,
                (long)memory.PrivateWorkingSet, (long)memory.PrivateUsage, (long)memory.WorkingSet,
                GC.GetTotalMemory(false), GC.GetTotalAllocatedBytes(precise: true), gc.TotalCommittedBytes,
                gc.HeapSizeBytes, gc.FragmentedBytes, process.HandleCount, process.Threads.Count,
                AppDomain.CurrentDomain.GetAssemblies().Length, GetGuiResources(process.Handle, 0),
                GetGuiResources(process.Handle, 1), GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
                process.TotalProcessorTime.TotalMilliseconds, GC.GetTotalPauseDuration().TotalMilliseconds));
        }

        Take("before-context");
        using var context = new TrayApplicationContext(controller,
            new SettingsStore(Path.Combine(directory, "settings.json")),
            new(new() { Notifications = new() { Enabled = false } }, false), showWindow: false);
        Take("after-context");
        var timer = (System.Windows.Forms.Timer)typeof(TrayApplicationContext)
            .GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(context)!;
        while (!timer.Enabled) await Task.Delay(10);
        timer.Stop();
        if (controller.Current.Error is not null) throw new InvalidOperationException(controller.Current.Error);
        Take("first-read");

        // Keep the real two-second timer and real render path. idle has exactly
        // the same initialized UI/database/command machinery but no later reads.
        if (variant != "idle") timer.Start();
        for (var i = 1; i <= normalSeconds / 10; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            Take("normal-" + i);
        }
        timer.Stop();
        if (variant != "idle")
        {
            await context.RefreshAsync();
            Take("before-accelerated");
            // Compress the read count, not claim that this is elapsed real time.
            for (var i = 1; i <= acceleratedReads; i++)
            {
                var started = Stopwatch.GetTimestamp();
                await context.RefreshAsync();
                latencies[i - 1] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (i % 200 == 0) Take("accelerated-" + i);
            }
            timer.Start();
        }
        for (var i = 1; i <= normalSeconds / 10; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            Take("settled-" + i);
        }
        timer.Stop();
        if (variant != "idle") await context.RefreshAsync();
        Take("before-gc");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Take("after-gc");
        await Task.Delay(TimeSpan.FromSeconds(5));
        Take("after-gc-idle");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var result = new
        {
            Variant = variant, Runtime = RuntimeInformation.FrameworkDescription,
            OperatingSystem = RuntimeInformation.OSDescription, GCSettings = new
            {
                System.Runtime.GCSettings.IsServerGC, System.Runtime.GCSettings.LatencyMode,
                HighMemoryLoadThresholdBytes = GC.GetGCMemoryInfo().HighMemoryLoadThresholdBytes
            },
            Configuration = GC.GetConfigurationVariables(),
            DeviceCount = devices.Length, NormalSeconds = normalSeconds, AcceleratedReads = variant == "idle" ? 0 : acceleratedReads,
            SettledSeconds = normalSeconds, Samples = samples, ReadLatencyMilliseconds = latencies
        };
        File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        var before = samples.FirstOrDefault(s => s.Phase == "before-accelerated");
        var after = samples.Last(s => s.Phase == "before-gc");
        Array.Sort(latencies);
        Console.WriteLine("GROWTH_RESULT " + JsonSerializer.Serialize(new
        {
            result.Runtime, result.Configuration,
            MeanReadMilliseconds = latencies.Average(), P95ReadMilliseconds = latencies[(int)(latencies.Length * 0.95)],
            CpuMilliseconds = before is null ? 0 : after.CpuMilliseconds - before.CpuMilliseconds,
            PauseMilliseconds = before is null ? 0 : after.PauseMilliseconds - before.PauseMilliseconds
        }));
        // Emit after the final observation so log serialization does not inflate
        // the measured allocation rate or trigger collections during the run.
        foreach (var sample in samples)
            Console.WriteLine("GROWTH_SAMPLE " + variant + " " + JsonSerializer.Serialize(sample));
    }

    private sealed class FixtureRunner(string json, string fixture, bool useProcess) : ICommandRunner
    {
        private readonly ProcessCommandRunner _runner = new();
        public int Reads { get; private set; }
        public async Task<CommandResult> RunAsync(CommandRequest request, CancellationToken cancellationToken)
        {
            if (!request.Arguments.SequenceEqual(new[] { "state" }))
                throw new InvalidOperationException("The growth probe permits state reads only.");
            var result = useProcess
                ? await _runner.RunAsync(new(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    ["/d", "/c", "type", fixture], request.Timeout), cancellationToken)
                : new CommandResult(0, json, "");
            Reads++;
            return result;
        }
    }

    private sealed record Sample(string Phase, double Seconds, int Reads, long PrivateWorkingSet,
        long PrivateCommit, long WorkingSet, long ManagedHeap, long TotalAllocated, long GcCommitted,
        long HeapAfterLastGc, long FragmentedAfterLastGc, int Handles, int Threads, int Assemblies,
        uint GdiObjects, uint UserObjects, int Gen0, int Gen1, int Gen2, double CpuMilliseconds, double PauseMilliseconds);

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

    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);
}
