# Protected namespace header binding, 26 October 2026

The virtualized catalog used to check every type each time it bound a namespace header with fewer than two removable types. The new catalog snapshot records namespace removal
eligibility during reconciliation. Unchanged header binding reads that result; catalog additions/removals rebuild it. Storage is bounded by the namespace count and owned by the
view.

## Source and method

Unity 6000.5.2f1 on Windows 11, Snapdragon X Elite X1E80100. The explicit `VirtualizedTypeCatalogTests.ShouldMeasureProtectedNamespaceHeaderBinding` driver verifies compiler source
checksums and loaded modules before and after each run. Both runs use identical driver/probe source, five discarded warm-ups and 30 samples at each size. Every sample binds one
unhosted header ten times.

[Baseline samples](protected-header-baseline-2026-10-26.csv) and [repaired samples](protected-header-final-2026-10-26.csv) preserve source hashes, module IDs and completion status.
The baseline renderer is identical to main's catalog renderer at ed905da; the repaired renderer is identified by its raw source hash. Benchmark driver and probe source are in this
change. The ordinary regression first fails with 20,000 checks, then passes with zero.

| Protected entries | Original batch p95 (ms) | Repaired batch p95 (ms) | Checks before / after |
| ----------------- | ----------------------- | ----------------------- | --------------------- |
| 100               | 0.5590                  | 0.2180                  | 1,000 / 0             |
| 1,000             | 4.4440                  | 0.2747                  | 10,000 / 0            |
| 2,000             | 7.8363                  | 0.1506                  | 20,000 / 0            |

p95 uses nearest rank across 30 ten-call samples. Counts cover assignability reads through the instrumented type wrapper. Each list repeats one protected `TestDataObject` type
wrapper; it is not a set of distinct CLR types or Unity assets. The wrapper adds observation overhead, and sequential runs do not isolate a causal wall-time speedup. There are no
allocation, native-memory, scrolling-frame or whole-editor stall claims. Canonical reconciliation still scans changed catalogs.

The functional regression also verifies zero/one/two removable entries and invalidation after removal. The existing hosted 2k-type regression passes filtering, collapse, recycling,
drag/order, focus, both state stores and six-theme layout. These results do not complete #324's real-project, isolated-allocation or Unity 2021.3/platform requirements.
