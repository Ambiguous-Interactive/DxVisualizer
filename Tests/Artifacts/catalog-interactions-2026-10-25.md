# Catalog filter and collapse observations, 25 October 2026

The existing hosted catalog driver now records filtering, clearing the filter and keyboard namespace collapse/expand separately. The
[baseline report](catalog-interactions-before-2026-10-25.json) completes all 180 observations in one warm Unity 6000.5.2f1 Windows process, with the dark editor skin and one pixel
per point. Inputs are synthetic CLR identities, not Unity assets or imported scripts.

## Method

Each size/distribution has five discarded warm-ups and 30 recorded repetitions. Sizes are 100, 1,000 and 2,000 types, grouped into one namespace or ten types per namespace.
Construction, scrolling and selection retain their existing clocks and row counts. The new phases apply only to the virtualized 1k/2k catalogs; compact 100-type observations retain
-1 sentinels for those fields.

After selection observations, the driver filters to the final exact type name and asserts two logical rows. It clears the filter, scrolls to the first namespace header, and sends
the configured collapse/expand keys. It verifies exact logical row counts after each transition and restores the expanded catalog. Call clocks stop after production returns;
observed clocks include a later editor update and layout settling. These are scripted component observations, not physical input or GPU presentation latency.

The unchanged ten-minute watchdog and 512 MiB additional retained-managed-heap guard remain in effect. The complete NUnit case passes in 86.01 seconds; report elapsed time is 83.53
seconds. Native editor hosts and window wrappers return to their starting counts. Compiler source checksums and loaded module identities are verified before and after the run and
retained in the raw report. The driver source SHA-256 is `5b6cf15ff17b502efcd92f49d746c26a01299aca0d39f163369edf471cc03a2c`.

## Baseline

Percentiles use nearest rank, `ceil(0.95 × 30)`. Values are call p95 in milliseconds.

| Types | Distribution            | Filter  | Clear filter | Collapse | Expand  |
| ----- | ----------------------- | ------- | ------------ | -------- | ------- |
| 1,000 | One namespace           | 17.2577 | 32.7917      | 19.0132  | 32.1549 |
| 1,000 | Ten types per namespace | 13.5991 | 25.6419      | 29.5535  | 26.1079 |
| 2,000 | One namespace           | 25.2921 | 35.5859      | 19.6298  | 35.8599 |
| 2,000 | Ten types per namespace | 19.1873 | 28.5570      | 32.9146  | 29.3555 |

## Limits

The rooted allocation control reports zero observed bytes for a 4 KiB allocation, so the per-thread allocation counter is unsupported. Whole-editor frame allocation, process heap
and Unity allocation fields do not isolate these interactions. Dynamic types remain loaded in the process. This is neither a fresh-process comparison nor a real-project, Unity
2021.3 or cross-platform matrix. The new phases do not measure catalog text-field event routing, user task time/errors or asset-change reconciliation.

Issues #306 and #324 remain open for their broader fixture/distribution/lifecycle, allocation, layout and platform acceptance. No support guarantee or release is added.
