# Polling memory comparison

The polling changes reduce temporary allocations, and a 64 MiB managed-heap
limit reduces the resident footprint in the measured workload. The two repeated
comparisons averaged 30.3 MiB before and 17.9 MiB after, a 41% reduction in
private working set. These are synthetic Windows CI measurements, not a
prediction of the exact footprint on every PC.

## Changes

- Cache parsed devices when a fresh successful `usbipd state` response is
  identical. Every poll still runs the command; changes and errors are observed
  immediately. The cached collection is read-only.
- Parse each device's VID/PID once and avoid temporary selection arrays.
- Rent reusable output buffers while draining stdout and stderr concurrently.
  Preserve Unicode output, cancellation, timeouts, and stream disposal.
- Set `System.GC.HeapHardLimit` to `67108864` for the application and its native
  UI tests. Verify the same setting in the packaged MSIX.

The [heap limit](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector#heap-hard-limit)
limits GC heap and bookkeeping commitment. It neither reserves 64 MiB at startup
nor caps the entire process's working set; native UI memory is outside it.
Normal application code does not force collections or trim its working set.
Future features that substantially increase retained managed data should
reassess this limit.

## Method

[Comparison run 46131320926](https://github.com/rubyu/keypivo/actions/runs/46131320926)
used self-contained .NET 10.0.15 x64 builds on Windows runners. Baseline commit
`48cfcb3d57c46b5c51f80892df512c66d492c26e` has the application code from 1.0.75.0
plus a diagnostic entry point. Optimized code was
`2a43350a2f8f6c82f00812bc3197a7a7bf173674`, with each candidate GC setting applied
to its runtime configuration.

Each repetition ran all four conditions sequentially on one runner, with a
fresh process for each condition. The second repetition reversed the order on
another runner. Both builds used the same diagnostic source and 16 synthetic
USB devices. The real tray controller, rendering path, and process-output
reader were exercised; a child `cmd.exe` returned fixture JSON instead of
accessing USB hardware. Notifications were disabled and the popup was hidden.

Each process ran the normal two-second timer for 60 seconds, performed 2,400
accelerated reads, then ran the normal timer for another 60 seconds. The
accelerated phase compresses read count, not elapsed time. Working set below
is the average of six samples in the final minute, before the diagnostic-only
collection at the end. Allocation per read is measured in the accelerated
phase. Sampled peaks can miss short-lived peaks.

## Results

| Condition | Private working set, run 1 / run 2 (MiB) | Sampled peak, run 1 / run 2 (MiB) | Allocation/read, run 1 / run 2 (KiB) |
| --- | ---: | ---: | ---: |
| Baseline | 28.74 / 31.80 | 38.24 / 38.06 | 66.49 / 68.43 |
| Allocation changes only | 32.15 / 28.10 | 35.71 / 35.69 | 42.41 / 43.34 |
| Allocation changes + `ConserveMemory=5` | 32.39 / 33.07 | 35.41 / 35.74 | 42.46 / 43.31 |
| Allocation changes + 64 MiB heap limit | 17.90 / 17.85 | 26.24 / 26.25 | 43.31 / 43.18 |

The selected configuration reduces allocation per read by 36% on average.
Allocation reduction alone does not reliably reduce resident memory: the GC
can retain and reuse heap capacity. `ConserveMemory=5` did not improve this
workload. The selected limit reduced the observed generation-zero maximum
budget from 16 MiB to 6 MiB on both comparison runners.

| Metric, average of two repetitions | Baseline | Selected configuration |
| --- | ---: | ---: |
| Private commitment after workload | 36.25 MiB | 22.56 MiB |
| Live managed heap after final diagnostic GC | 3.29 MiB | 3.33 MiB |
| Parent-process CPU during accelerated reads and final minute | 3.91 s | 4.02 s |
| Total GC pause during that interval | 14.14 ms | 15.74 ms |
| Mean read duration | 12.45 ms | 13.37 ms |
| 95th-percentile read duration | 14.34 ms | 15.03 ms |

More frequent collections trade a small CPU/latency increase in this sample
for a lower footprint. Child-process CPU is excluded. The two repetitions
are insufficient to establish a precise performance regression percentage.
All native UI checks also passed under the selected limit, including settings,
notifications, setup, polling, switching simulation, and long descriptions.

## Reproduction

The `Memory optimization comparison` workflow repeats the baseline and selected
configuration on two runners. Its optional `include_candidates` input also
runs the allocation-only and `ConserveMemory=5` conditions. The JSON artifacts
record runtime settings, samples, GC counts, and read latencies. The ordinary
CI suite verifies functional behavior, native UI behavior under the selected
policy, and the packaged runtime configuration.

The benchmark is deliberately isolated from real devices. Store installation,
long idle periods, real `usbipd` output, UI usage, OS memory pressure, and CPU
characteristics can change the resulting working set. A longer measurement of
the updated Store build is still needed to establish its footprint on a
particular PC.
