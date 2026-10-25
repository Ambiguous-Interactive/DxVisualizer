# Catalog control binding, 25 October 2026

The [baseline](catalog-controls-baseline-2026-10-25.json) and [final](catalog-controls-final-2026-10-25.json) reports each complete all 180 observations in one warm Unity
6000.5.2f1 Windows process, dark editor skin, one pixel per point. The existing driver creates synthetic CLR identities, not Unity assets or imported scripts. Each
size/distribution has five discarded warm-ups and 30 retained repetitions. The ten-minute watchdog and 512 MiB additional retained-managed-heap guard remain unchanged.

The baseline uses main at `05c1bcf`. The final production tree is `789f850`: it adds catalog keyboard focus navigation and retains direct references to each realized row's
controls. Binding no longer queries the visual tree for the title, collapse, ordering and remove controls. References belong to realized rows, not logical entries; callback targets
still read the current row identity.

## Separate scroll observations

The unchanged observer records the scroll-offset setter call, accumulated production binding time and a later settled-layout observation. Binding can occur after the setter
returns. The binding wrapper adds two timestamp reads per call. Installation settles before counters reset; counters stop before restoring the original delegate. Compact 100-type
cases keep `-1` binding sentinels.

Nearest-rank p95 uses `ceil(0.95 × 30)`. All values below are milliseconds, baseline → final. Every measured virtualized scroll binds 18 rows, moves the viewport and intersects
realized type rows. Both runs retain at most 20 realized catalog rows at 1,000 and 2,000 types.

| Types | Distribution            | Scroll call     | Binding         | Later layout       |
| ----- | ----------------------- | --------------- | --------------- | ------------------ |
| 1,000 | One namespace           | 1.3979 → 0.8154 | 7.6360 → 4.8042 | 55.6303 → 52.2165  |
| 1,000 | Ten types per namespace | 0.6985 → 1.1477 | 6.0329 → 6.9107 | 39.7312 → 64.9058  |
| 2,000 | One namespace           | 0.7640 → 1.0131 | 5.8656 → 5.7493 | 129.3069 → 50.1416 |
| 2,000 | Ten types per namespace | 0.8116 → 0.7020 | 7.3727 → 5.4395 | 50.6698 → 48.5849  |

These sequential warm-process observations show mixed binding and layout results. They do not establish a uniform or end-to-end scrolling speedup. No extra run was selected to
improve the comparison.

## Provenance and behavioral checks

The raw files preserve emitted bytes, compiler/source checksums and module identities. Both runs verify their source identities before and after measuring and return native
hosts/window wrappers to their initial counts. Baseline NUnit/report duration is 94.09/91.468 seconds; final is 95.01/91.698 seconds.

The unchanged driver source SHA-256 is `1eb84d16d94cbb6e89a91aee0b3206bea0d6b764b9ba953d64100218538a5ff8`. Baseline renderer SHA-256 is
`6b9b8ca03c355646ccbc67aa51781df527dc25662e989d33e3eab606755978f8`; final renderer is `fc41f40277cf05a20fa7509dc31b9dc2eb74ded9eb1190cc223caf750ea477ae`. The test assembly was
recompiled between runs for the hosted keyboard regression; identical driver source does not mean identical assembly bytes.

The hosted keyboard regression fails on original code at header Down and passes after repair. It routes keys through the window handler and checks header focus, an offscreen End
jump, filtered Home/End and unchanged selection. Existing coverage checks both persistence stores, six shipped themes, collapse, canonical ordering, stale pointer release and exact
identities while recycling. These scripted checks do not replace exploratory add/find/clone/rename/move/delete task times or error counts.

## Limits

The rooted 4 KiB allocation control reports zero, so per-thread allocation is unavailable. Whole-editor frame allocation and process/native heap fields do not isolate binding or
scrolling. Removing tree queries does not prove zero allocation.

The observations do not establish physical input latency, GPU presentation, a hard 4 ms editor-frame bound, Unity 2021.3 execution, light editor-skin behavior or
real-project/platform acceptance. Issues #306, #312 and #324 retain those broader requirements. No real-asset support expansion, merge or release is claimed.
