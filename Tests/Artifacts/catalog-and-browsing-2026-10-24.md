# Catalog and browsing observations, 24 October 2026

These are warm-process observations on Unity 6000.5.2f1 Windows. They do not establish Unity 2021.3 execution, other-platform performance, fresh-process behavior or a large-project
support guarantee. Source/module checks and raw emitted reports distinguish component work from actual assets. The [provenance record](catalog-browsing-provenance-2026-10-24.json)
binds the loaded assemblies to compiler source checksums and retains cleanup/test outcomes. The measured C# tree is f908dc7; later stack commits change only prose and artifacts.

## Catalog construction and scrolling

[Raw catalog samples](catalog-layout-2026-10-24.json) contain all 180 measured observations: five discarded warm-ups and 30 samples for each size/distribution. The experiment
passed in 280.07 seconds under its declared ten-minute limit and 512 MiB additional retained-managed-heap guard. Native editor hosts and window wrappers returned to their starting
counts.

The production `NamespaceController.Build` creates the UI in a hosted, themed 360 × 720 window. Inputs use 4,000 generated plain CLR class identities, not ScriptableObjects or
imported scripts. Half support each namespace distribution; each condition uses a prefix of 100, 1,000 or 2,000 identities. Their names and namespace keys are distinct. No Unity
assets are created or instantiated. These CLR types remain in this editor process; the report is synthetic catalog work, not asset discovery, activation, mutation or real-project
type registration.

| Types | Distribution            | Construction p95 (ms) | Observed scroll p95 (ms) | Instantiated elements | Instantiated type rows | Rows intersecting viewport | Editor-frame allocation p95 (bytes) |
| ----- | ----------------------- | --------------------- | ------------------------ | --------------------- | ---------------------- | -------------------------- | ----------------------------------- |
| 100   | One namespace           | 20.6242               | 24.2018                  | 510                   | 100                    | 27                         | 1,974,625                           |
| 100   | Ten types per namespace | 20.8207               | 21.1933                  | 591                   | 100                    | 25                         | 3,563,900                           |
| 1,000 | One namespace           | 344.3155              | 126.0345                 | 5,010                 | 1,000                  | 27                         | 30,619,671                          |
| 1,000 | Ten types per namespace | 363.1577              | 146.1845                 | 5,901                 | 1,000                  | 25                         | 34,568,392                          |
| 2,000 | One namespace           | 566.7341              | 235.7378                 | 10,010                | 2,000                  | 27                         | 58,501,251                          |
| 2,000 | Ten types per namespace | 737.5590              | 273.8278                 | 11,801                | 2,000                  | 25                         | 66,925,678                          |

Percentiles use nearest rank, `ceil(0.95 × 30)`. Construction includes a new controller and the production tree build, before attachment/layout. Scrolling measures the offset
change through a subsequent editor update and settled layout; every sample moved the viewport. It includes scheduling and observation delay, not GPU presentation. Viewport counts
are rectangle intersections, not verified pixel visibility.

The per-thread allocation counter failed a rooted 4 KiB positive control. Its construction-allocation values are `-1`, not zero. The separate frame values use Unity's
[ProfilerRecorder](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorder.html) counter and are the last completed whole-editor frame
observed after construction. They include other editor and observer work and do not isolate package allocation. Managed heap and Unity allocation fields are process observations,
not an isolated/native retention slope.

This remains an unvirtualized catalog. All type rows exist even when only 25–27 intersect the viewport. Namespace ordering alone cannot remove that growth. #324 remains open for
flattened virtualization, recycling focus, interactions and the remaining layout/order acceptance.

## Namespace ordering

The explicit paired component experiment compares the former insertion algorithm with production ordering at 100/1k/10k namespace keys, with forward, reversed and tied ranks. Each
condition uses five warm-ups and 30 samples; pair order alternates. It creates no Unity objects, assets or UI. The production path keeps stable ties and shared type lists, avoids
scratch for already ordered ranks, and sorts unsorted ranks by rank plus input index.

[Final ordering samples](catalog-ordering-2026-10-24.json) contain all 540 measured observations and verified pre/post source identities. The explicit test passes in 322.08
seconds; component elapsed time is 320.57 seconds. Both the ten-minute component bound and explicit 650-second test timeout remain intact.

| Namespaces | Order   | Former p95 (ms) | Production p95 (ms) |
| ---------- | ------- | --------------- | ------------------- |
| 100        | Forward | 0.0847          | 0.0728              |
| 100        | Reverse | 0.8014          | 0.1821              |
| 100        | Ties    | 0.3801          | 0.2790              |
| 1,000      | Forward | 0.8155          | 0.6423              |
| 1,000      | Reverse | 65.0964         | 2.2063              |
| 1,000      | Ties    | 29.2070         | 2.4811              |
| 10,000     | Forward | 8.1501          | 6.8196              |
| 10,000     | Reverse | 6,784.0892      | 24.1908             |
| 10,000     | Ties    | 2,957.2819      | 28.5768             |

These are paired component timings in one warm process, not whole-window or cross-platform speedups. Ten-thousand-namespace sorts can still exceed an editor slice budget. The
rooted allocation control reports zero observed bytes, so every per-call allocation sample is `-1` (unavailable). The ordered path removes key scratch by construction; unsorted
input uses eight-byte keys instead of the former four-byte ranks. This is not a measured allocation total.

Two earlier attempts are excluded from the table: the [five-minute stop](catalog-ordering-five-minute-stop-2026-10-24.json), and the
[completed component matrix](catalog-ordering-nunit-timeout-2026-10-24.json) whose [NUnit result failed](catalog-ordering-nunit-result-2026-10-24.json) at the default 180-second
timeout. The latter raw file's `complete=true` describes its method, not a passing test; its zero allocation observations are unsupported. Raw bytes remain unchanged.

## Keyboard capture

![Namespace collapse arrow with keyboard focus](namespace-collapse-focus.webp)

The 1180 × 780 hosted capture shows the focused namespace arrow. It uses copied window state and restores preferences/selection ownership; the window closes through the native
cleanup helper. The layout regression also checks focused glyph bounds, padding and a long localized namespace under all six shipped themes. This is automated layout evidence, not
a timed usability study or light-editor/platform matrix. #312 retains those acceptance items.

## Browsing distributions

The existing `PerfBaseline` driver can omit query samples while retaining indexing/readiness, selection, label filtering, scrolling and type switches. Reports mark
`searchMeasurementsEnabled=false`; omitted query arrays are empty and configured query repetitions are zero. A one-type distribution omits type-switch measurements. These omissions
do not report zero latency.

The [initial ordinary-SO run](browsing-ordinary-staging-attempt-2026-10-24.json) completes all 40 readiness and 70 interaction observations at 1,000 fixtures, with zero query
observations. Its 10k fixture stage is intentionally cancelled at 1,237/10,000, not counted as a completed dataset. That cancelled stage records 223.60 seconds total, 5.29 seconds
in creation calls and 150.61 seconds flushing import batches. The next experiment declares 64 ms/100-item staging limits instead of 4 ms/10 items; import calls remain indivisible,
and neither budget guarantees an editor-frame limit.

| Fixture type | Fixtures | Warm usable rows p95 (ms) | Warm inspector p95 (ms) | Warm settled index p95 (ms) | Loaded rows | Realized rows | Strong global-search rows |
| ------------ | -------- | ------------------------- | ----------------------- | --------------------------- | ----------- | ------------- | ------------------------- |
| Ordinary SO  | 1,000    | 500.7327                  | 500.7327                | 2,005.4500                  | 1,000       | 18            | 0                         |

This table uses 30 warm observations after five warm-ups; the five cold windows still share the warm editor process. Physical input-to-display latency and GPU presentation are not
measured. Selected rows still retain the full type population; zero strong global-search references does not establish bounded selected-object retention. The initial run returns to
zero owned fixture roots/browser windows, seven editor windows/one native host and the same 231 pre-existing SO assets, with matching pre/post source/module checks.

The [larger-batch ordinary 10k attempt](browsing-ordinary-10k-2026-10-24.json) creates all 10,000 fixtures and completes all 70 interactions, but hits its declared twelve-minute
watchdog while waiting for warm readiness. It retains 34 readiness records: 33 complete, one active/incomplete, with only 23 complete warm observations instead of the required 30.
This is an incomplete run, not a completed 10k readiness matrix. No readiness percentile is computed from that partial population.

Both completed interaction conditions retain five warm-ups and 30 samples. Label-filter observed-ready p95 is 53.1874 ms; scrolling observed-ready p95 is 38.5264 ms. These include
scheduling/observation and do not establish GPU presentation or a general latency bound. Completed readiness observations load all 10,000 selected-type objects with at most 18
realized rows and zero strong global-search references. The in-progress final readiness record loads fewer rows and remains incomplete. Matching post-run code checks and the same
zero-root/window/asset-count cleanup pass.

The [BaseDataObject run](browsing-base-2026-10-24.json) completes all 40 readiness and 70 interaction observations at 1k. At 10k it creates all fixtures and completes all 70
interactions, then stops at its twelve-minute watchdog with 31 readiness records: 30 complete and one active/incomplete, including only 20 complete warm observations. It is not a
completed 10k readiness matrix. The completed 10k label-filter and scroll conditions each contain five warm-ups and 30 samples, with observed-ready p95 of 57.8036 and 42.6779 ms.
Completed readiness observations load all 10,000 selected rows, with at most 18 realized rows and zero strong global-search references.

Both larger-batch runs use a declared twelve-minute cap and 64 ms/100-item staging settings. No queries run. Base cleanup returns to the same zero owned roots/browser windows,
seven windows/one host and 231 pre-existing SO assets. Pre/post compiler/source/module checks match. No C# source changed during these runs or the final suites; all 877 EditMode
and 15 actual PlayMode cases pass. The exact npm audit contains 261 byte-identical allowed files and excludes test evidence. Focus was observed active during ordinary
staging/interaction, not controlled for every sample. These are warm-process observations with synthetic CLR metadata still resident, not isolated memory or controlled cross-host
comparisons. #306 retains the full fixture/distribution, change-operation, matched Play/compile, fresh-process and platform matrices. #309 retains bounded selected rows and
native/cache costs. No issue is closed by this bounded report.
