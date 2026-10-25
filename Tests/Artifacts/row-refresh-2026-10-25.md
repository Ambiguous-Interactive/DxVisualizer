# Changed asset-row binding costs

This bounded follow-up for #308 and #312 removes a full filtered-list scan from an existing visible-row refresh. It does not complete either issue's broader acceptance.

## Production behavior and coverage

The targeted refresh path used `IndexOf` to pass a display index into `BindObjectRow`. That method only needed the index to identify first/last rows. It now compares the bound
object directly with the filtered list's endpoints, and both callers omit the index. No index cache or additional retained state is introduced. Binding still updates titles, hints,
selection decoration and ordering affordances; action semantics remain unchanged.

The expanded hosted refresh regression passes before and after the change. It checks first/last/single-row arrow states through ordering, a unique-label filter and an incremental
last-row edit. Existing checks preserve selection, load generation, scroll position and unchanged neighboring bindings. The full 21-case mutation/refresh fixture passes, including
actual Play entry/exit with reload disabled, remapped ordering shortcuts, hidden membership and both persistence stores. An initial expanded-test failure counted a queued reorder
focus update as refresh work; removing that earlier setup mutation restores the original nonstructural-edit observation. It is not a production regression or performance result.

## Component probe

The [unchanged probe](row-refresh-probe-2026-10-25.cs.txt) uses an unhosted production row and two transient ScriptableObjects. Each synthetic list contains repeated references to
one filler and one final target. Thus 100k entries do not mean 100k distinct objects or assets. The probe isolates synchronous binding plus the old caller's removed `IndexOf`; it
excludes asset intake, metadata reconciliation, selection refresh, panel layout and paint. The adaptive delegate calls the old indexed or new endpoint-based production binding
signature. Reflection setup, fixture construction and production identity checks remain outside the timed batches.

Each condition has five warm-ups and 30 measured ten-call batches, for 90 retained samples per run. A 30-second sampling guard is checked before each batch. Production compiler
source/module and assembly checks pass before and after sampling. The owner and two transient objects are disposed; editor-window wrapper counts return to seven in both runs. No
window is shown and no fixture asset is created. Owner setup can resolve the existing editor configuration outside the timed batches.

| Synthetic entries | Baseline median / p95, ten calls (ms) | Final median / p95, ten calls (ms) |
| ----------------- | ------------------------------------- | ---------------------------------- |
| 1,000             | 1.8578 / 2.1944                       | 0.5114 / 0.7627                    |
| 10,000            | 15.4794 / 19.3303                     | 0.4874 / 0.6536                    |
| 100,000           | 139.4408 / 145.9721                   | 0.4520 / 0.7472                    |

Times are for ten calls, not individual-call p95. The target is deliberately last, so the removed lookup scans the full list. These sequential warm-process measurements support
removing that scan; they are not controlled whole-window speedups, physical-input latency, allocation/native-memory measurements or evidence of real 100k-asset support.

[Baseline samples](row-refresh-baseline-2026-10-25.json) use production source SHA-256 `82580c044d2e6627811b9bd567d36f6f5596dcad6ab83189eba82adfe5bf1c79`;
[final samples](row-refresh-final-2026-10-25.json) use `b99dd59fb8925cf92a13e419ccdbd3e3c3a2b57cff5c23c4336ccdef50c5b9b4`. Reports retain loaded module IDs. Probe SHA-256:
`1fe8e6e46f098b093bff46f740c25d15728b2775d8de9e670d0e1efc999d6654`.

Both runs use Unity 6000.5.2f1 on Windows 11 build 26200 in one warm process. The wider real-asset, domain/script/type-catalog, usability, light-editor, Unity 2021.3 and platform
matrices stay with #308, #312 and #306. No acceptance box or support guarantee is promoted.
