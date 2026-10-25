# Label controls on 25 October 2026

Refs #308, #312 and #306. Repeated label-filter binding rebuilt every pill, including unchanged labels. A hosted regression fails on main at 111f769 when an unchanged button is
replaced. The repaired renderer reconciles sorted ordinal label identities, updates active state and preserves surviving controls. Removing the focused label returns focus to the
mode selector. Missing active labels remain removable; presentation does not rewrite saved filters.

The actual asset-refresh regression also passes: adding another label through `QueueAssetChanges` keeps the existing filter button and keyboard focus, without rebinding unchanged
asset rows. All 18 label-editor cases pass, including six-theme narrow-pane checks. The new fixture covers case-distinct labels, duplicates, additions/removals, missing selected
labels, active styles, Advanced mode and unbound configuration.

## Repeated binding component observations

[Baseline CSV](label-binding-baseline-2026-10-25.csv) and [final CSV](label-binding-final-2026-10-25.csv) retain all 90 measured observations per revision. The unchanged explicit
`LabelFilterEditorTests.ShouldMeasureRepeatedLabelBinding` driver uses five discarded warm-ups and 30 measured binds at each label count. Each bind receives the same empty filter
configuration and available labels. Replacement counts compare each control reference before and after the call; these are operation counts, not allocated byte measurements.

| Available labels | Baseline median / p95 ms | Final median / p95 ms | Replaced controls per bind, baseline / final |
| ---------------- | ------------------------ | --------------------- | -------------------------------------------- |
| 10               | 0.7148 / 1.4068          | 0.1676 / 0.3235       | 10 / 0                                       |
| 100              | 7.6898 / 11.1924         | 1.1071 / 1.6778       | 100 / 0                                      |
| 1,000            | 80.6495 / 192.7426       | 9.0978 / 11.3776      | 1,000 / 0                                    |

P95 uses nearest rank (29th of 30 sorted observations). Both runs use one warm Unity 6000.5.2f1 Windows 11 process on Snapdragon X Elite. They create no assets or native windows.
The CSV headers retain production and driver SHA-256 values plus loaded module identities; `BenchmarkCodeIdentity.Verify` checks compiled source before and after each run. Driver
source is identical across the two runs. The later real-asset integration assertion changes the test assembly after these observations; it does not change the measured renderer or
driver source.

These are sequential unhosted component observations. They do not measure paint, import duration, input-to-ready latency, allocated bytes, native memory or whole-window stalls.
They do not establish a controlled end-to-end speedup, real 100k-asset support, light-editor or Unity 2021.3 execution. The broad parent issues remain open.
