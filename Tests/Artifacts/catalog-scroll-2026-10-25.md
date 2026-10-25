# Catalog scroll binding observations, 25 October 2026

The [baseline report](catalog-scroll-before-2026-10-25.json) completes all 180 observations in one warm Unity 6000.5.2f1 Windows process, dark editor skin, one pixel per point.
Inputs are synthetic CLR identities, not Unity assets or imported scripts. Five warm-ups and 30 measured repetitions cover 100, 1,000 and 2,000 types in one namespace or ten types
per namespace. The unchanged ten-minute watchdog and 512 MiB additional retained-managed-heap guard apply.

The existing driver now separates three scroll clocks: the scroll-offset setter call, accumulated production row-binding time, and the later settled-layout observation. Binding can
occur after the setter returns. The binding wrapper adds two timestamp reads per call. Installing it can itself rebind rows; a later layout settles before counters reset. Compact
100-type catalogs retain -1 binding sentinels. Every measured virtualized scroll moves, binds new identities and realizes type rows intersecting the viewport. Counters stop before
restoring the original binding delegate.

Nearest-rank p95 uses `ceil(0.95 × 30)`. All timings are milliseconds; row counts are per scroll.

| Types | Distribution            | Scroll call | Binding | Later layout | Bound rows |
| ----- | ----------------------- | ----------- | ------- | ------------ | ---------- |
| 1,000 | One namespace           | 1.1479      | 9.7232  | 51.5671      | 18         |
| 1,000 | Ten types per namespace | 1.0120      | 10.3397 | 68.4201      | 18         |
| 2,000 | One namespace           | 1.8262      | 11.8037 | 69.4059      | 18         |
| 2,000 | Ten types per namespace | 1.1580      | 27.5404 | 114.5784     | 18         |

NUnit passes in 117.93 seconds; the report clock is 113.56 seconds. Compiler source checks and loaded module identities are verified before and after the run; native hosts and
window wrappers return to their initial counts. The report preserves the original emitted bytes, checksums and module identities. The baseline includes the captured-row gesture
repair; it still scans canonical siblings to decide whether ordering buttons are enabled.

The rooted 4 KiB allocation control reports zero, so per-thread allocation is unsupported. Whole-editor frame allocation and process/native heap fields do not isolate scrolling.
These scripted warm-process component observations do not establish physical input latency, GPU presentation, zero allocation, a 4 ms editor-frame bound, a usability study, Unity
2021.3 execution or a real-project/platform matrix. Issues #306, #312 and #324 retain their broader acceptance criteria.
