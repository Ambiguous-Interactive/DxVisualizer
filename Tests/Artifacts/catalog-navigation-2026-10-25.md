# Filtered catalog navigation

Issue #324. These are paired component timings in one warm Unity 6000.5.2f1 Windows 11 process on Snapdragon X Elite. Synthetic CLR identities create no Unity assets or native
windows. They do not measure physical input, painting, scrolling, allocation, full-window latency, or Unity 2021.3 compatibility.

The explicit `VirtualizedTypeCatalogTests.ShouldMeasureFilteredCatalogNavigation` compares the previous nested scan with production navigation over the same ordered types and
visible rows. Both use concrete lists. Each direction skips the hidden tail before wrapping. The visible prefixes contain 10, 100 and 1,000 types. Five warm-ups precede 30 samples
per implementation/count/direction; paired order alternates. All 360 measured results select the expected exact identity.

| Types | Forward | Previous p95 ms | Production p95 ms |
| ----- | ------- | --------------- | ----------------- |
| 100   | True    | 0.1731          | 0.0914            |
| 100   | False   | 0.1895          | 0.1258            |
| 1000  | True    | 20.7318         | 0.6596            |
| 1000  | False   | 14.2695         | 0.5953            |
| 2000  | True    | 152.9995        | 1.2195            |
| 2000  | False   | 146.0279        | 1.3100            |

[Raw emitted samples](catalog-navigation-2026-10-25.csv) preserve source SHA-256, loaded module IDs and completion status. Compiler checksums, disk source and loaded assemblies
were verified before and after measurement. The raw file SHA-256 is `84ec903109eec90d81ba4fb6302528142c6a67a6c1fd601be27835a2b48731b8`.

The hosted regression also passes for hidden starting types, single-result and empty filters, namespace-local wrapping, collapse, recycled focus and both stores. #324 remains open
for its wider allocation/layout/platform acceptance. An initial interface-based control boxed row values; it is excluded from these results. The retained control uses the original
concrete-list access.
