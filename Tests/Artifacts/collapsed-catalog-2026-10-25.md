# Collapsed catalog filtering on 25 October 2026

Refs #324 and #306. A collapsed namespace needs one matching type to remain visible. The previous projection still looked up every hidden type's display title after finding a
match. The repaired projection skips later title checks in that group. It still walks the canonical entries and preserves their order; no cache, index or logical storage is added.

The operation-count regression fails before repair (100 checks instead of one) and passes afterward. It also covers a late match, no match, clearing the filter and expanded
results. Both catalog tests pass, including the existing hosted 2,000-type checks across grouping, filtering, collapse, recycling, keyboard/pointer actions, focus, ordering, both
persistence stores and six themes. These tests do not complete the wider platform or usability criteria.

## Paired component observations

[Baseline CSV](collapsed-catalog-baseline-2026-10-25.csv) and [final CSV](collapsed-catalog-final-2026-10-25.csv) retain all 180 measured observations per revision. The unchanged
`ShouldMeasureCollapsedCatalogFiltering` driver uses five discarded warm-ups and 30 measurements per size/state, alternating two filters that match every generated type. Each call
projects either one collapsed header or the header plus all expanded types. `CatalogTypeProbe` delegates actual generated CLR type metadata and counts display-attribute `IsDefined`
calls. The counts do not measure allocated bytes.

| Types | Group state | Baseline median / p95 ms | Final median / p95 ms | Attribute checks, baseline / final |
| ----- | ----------- | ------------------------ | --------------------- | ---------------------------------- |
| 100   | Expanded    | 1.2544 / 3.1930          | 0.9992 / 1.4213       | 100 / 100                          |
| 100   | Collapsed   | 1.2031 / 3.0364          | 0.0836 / 0.1967       | 100 / 1                            |
| 1,000 | Expanded    | 11.7107 / 14.3653        | 10.2474 / 15.4656     | 1,000 / 1,000                      |
| 1,000 | Collapsed   | 11.0010 / 13.7804        | 0.4811 / 0.6365       | 1,000 / 1                          |
| 2,000 | Expanded    | 21.5642 / 26.9339        | 22.8453 / 30.6284     | 2,000 / 2,000                      |
| 2,000 | Collapsed   | 25.1722 / 34.9186        | 0.8995 / 1.1409       | 2,000 / 1                          |

P95 is nearest rank, the 29th of 30 sorted observations. Both runs use one warm Unity 6000.5.2f1 Windows 11 process on Snapdragon X Elite. The CSV headers retain production, driver
and probe SHA-256 values and loaded assembly modules. Compiler/source checks pass before and after each run. Driver and probe source bytes and their loaded test module are
identical across the runs; only the renderer source/module changes.

These sequential synthetic, unhosted observations create no assets or native windows. They exclude paint, scrolling, input-to-ready latency, imports, allocation and native memory.
Expanded timings vary and sometimes worsen; there is no uniform or controlled end-to-end speedup claim. The source still traverses the canonical group, so it does not make
filtering constant-time or guarantee a 4 ms editor frame. Late/absent matches still check all necessary titles. Real-project, light-editor and Unity 2021.3 execution remain
unverified.
