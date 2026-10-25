# Cooperative selected-object loading, 25 October 2026

Selected-type loading now queues saved selection first, then canonical custom/path order, and attempts at most 100 GUIDs within a cooperative 4 ms elapsed budget. Initial and
background loading share that budget; the next slice is scheduled after the current frame. This removes the previous unconditional first 100-object batch and immediate second
batch. A slow indivisible Unity operation can exceed the budget.

The queue retains its current item until processing completes. A pause leaves that item available for retry; a generation change leaves replacement work untouched.
Generation/type/suspension checks also guard resolved assets, metadata extraction and later selection callbacks. Canonical insertion and saved selection remain independent of
loading order.

## Deterministic coverage

Nine tests use controlled Stopwatch-frequency ticks to cover elapsed yielding, an over-budget item, minimum progress, count caps, full drain, invalid budgets, generation
replacement and suspension during processing. Tests do not sleep or infer deadlines from machine speed. The queue helper returns attempted items, including an attempt interrupted
by cancellation. A retrying processor must keep its pre-completion effects idempotent; window insertion and loaded-object tracking already do so.

## Completed ordinary-SO observation

The [raw report](object-loading-ordinary-1k-2026-10-25.json) records all 1,000 `OtherEditorOnlyCreationData` assets in one large type, all 40 opening records and all 70
label-filter/scroll observations. Opening uses five cold-window observations in one warm process, five discarded warmups and 30 warm samples. Each supported interaction has five
warmups and 30 measured samples. Separate selection samples also use five warmups and 30 measurements. Search measurements are disabled; query observations are empty.

The existing driver's eight-minute watchdog and 4 ms/10-item staging settings stay unchanged. This run completes; it is not a fresh-process control or a matched before/after
speedup experiment. It uses Unity 6000.5.2f1 Windows on the Snapdragon X Elite host, dark editor skin, one pixel per point and one display. Exact environment fields and
process-wide memory observations remain in the raw report.

Warm p95 is 318.1180 ms for first root paint, 458.4127 ms for usable rows/inspector readiness and 6120.4228 ms for settled indexing/readiness. These clocks observe the existing
driver callbacks, not physical input or GPU presentation. In all 40 opening records, usable rows are observed before both the legacy cache-ready clock and settled readiness. The
100 ms opening target remains unmet. This is evidence of progressive opening, not a hard whole-editor frame bound.

All completed readiness records retain 1,000 loaded selected-type objects, realize 18 rows and retain zero strong global-search objects. Total selected-object retention is still
unbounded. The new budget does not cover synchronous discovery, candidate normalization, canonical sorting, view rebuilding or arbitrary user callbacks. Those and
persistent-cache/native-memory/platform acceptance remain in #309/#306.

## Failed nonmatching-script input

The [earlier failed input](object-loading-nonmatching-script-failure-2026-10-25.json) uses `EditorOnlyCreationData`, the deliberate multiple-Unity-type/nonmatching-filename
regression fixture. It creates all 1,000 assets and records five cold openings, then stops when selection cannot reload `Skill Tree 000062.asset`. It is excluded from ordinary-SO
conclusions. The report preserves its original error, phase and incomplete results; it is not a completed matrix or proof of the exact reload failure's cause. No failed-input
production repair is inferred from this experiment.

## Validation and limits

The final full EditMode invocation passes all 894 tests in 746.49 seconds. PlayMode passes all 15 tests in 0.38 seconds, including its actual-Play assertion. Nine focused budget
tests pass. The full 16-step non-Unity gate, strict documentation build and package whitelist audit pass.

The first full EditMode invocation passes 893 of 894 tests: `ShouldSkipUnsupportedRareFieldsDuringLiveRebuilds` reaches the existing five-minute driver watchdog in
`WaitingWarmReady`. Its focused rerun passes in 211.05 seconds, then the complete suite passes with identical source and unchanged deadlines. The original failure remains in the
validation record. The failed fixture cleans its temporary output, so its inner benchmark report is unavailable. These retries do not establish the exact timeout cause or eliminate
timing sensitivity.

[Validation and source proof](object-loading-validation-2026-10-25.json) bind the loaded code to the compiler's source checksums, module/assembly identities and committed bytes.
Source remains frozen throughout the accepted run and suites. The legacy driver's source is unchanged; no substitute measurement harness is added. The failed input and successful
raw report retain their emitted bytes.

No runtime API, saved GUID order, settings key or authored ID semantics change. This work does not complete #309's retained-object/cache/recovery or full lifecycle/platform
criteria, or #306's large-asset/distribution/operation matrices. There is no Unity 2021.3 execution or broad support guarantee, and no release is published.
