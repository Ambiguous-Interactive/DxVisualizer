# Editor-skin checks on 25–26 October 2026

Refs #324, #312 and #306. The previous catalog observations only used the dark native editor. These runs use the existing, unchanged
`CatalogLayoutScaleTests.ShouldMeasureLargeCatalogConstructionAndScrolling` driver on Unity 6000.5.2f1 Windows 11 / Snapdragon X Elite, with the same compiled renderer and driver
in both editor skins. No Unity assets are staged by that driver.

## Catalog observations

[Dark report](catalog-skins-dark-2026-10-26.json) and [light report](catalog-skins-light-2026-10-26.json) each contain all 180 observations: five discarded warm-ups and 30
measurements at 100/1k/2k CLR types, in one namespace or ten types per namespace. `editorIsProSkin` is true/false respectively; both report complete experiments and verified
native-window cleanup. Source hashes and loaded modules agree between the reports. Both retain timings, realized elements, scrolling movement and whole-editor frame allocation.

| Types / distribution    | Dark scroll p95 ms | Light scroll p95 ms | Dark / light frame-allocation p95 bytes | Realized catalog rows / elements               |
| ----------------------- | ------------------ | ------------------- | --------------------------------------- | ---------------------------------------------- |
| 100 / one namespace     | 46.9452            | 21.5634             | 2,058,097 / 1,993,216                   | Compact renderer: 100 type rows / 510 elements |
| 100 / ten per namespace | 25.9907            | 21.7094             | 3,868,020 / 3,600,554                   | Compact renderer: 100 type rows / 591 elements |
| 1k / one namespace      | 127.6115           | 127.6167            | 785,463 / 801,879                       | 20 / 165                                       |
| 1k / ten per namespace  | 48.6115            | 36.8200             | 868,444 / 1,793,157                     | 20 / 165                                       |
| 2k / one namespace      | 50.2043            | 126.6507            | 883,573 / 851,063                       | 20 / 165                                       |
| 2k / ten per namespace  | 43.0951            | 132.1450            | 3,000,115 / 1,066,793                   | 20 / 165                                       |

P95 uses nearest rank. Every scroll moved the viewport and intersected type rows. The per-thread allocation counter fails its rooted 4 KiB control (observed zero), so those values
remain unsupported `-1`, not zero allocation. The available frame counter includes other editor/observer work. Sequential skin timings vary and do not establish causal speedups,
isolated package allocation, a frame budget or real-project support.

The catalog reports and captures precede the later native-field color repair. They do not measure that repair's performance. The repair adds selectors for popup/object value text
and popup arrows; catalog rendering code is unchanged. [Dx dark](catalog-skins-dark-Dx-2026-10-26.png), [Dx light](catalog-skins-light-Dx-2026-10-26.png),
[Classic dark](catalog-skins-dark-Classic-2026-10-26.png) and [Classic light](catalog-skins-light-Classic-2026-10-26.png) retain the actual 360 × 720 captures.

## Field readability and fixture isolation

The [original light full-window capture](layout-light-before-2026-10-26.png) shows black dropdown text on the dark package background. The theme stylesheet covered the legacy popup
text class but missed Unity's `unity-base-popup-field__text`; object-reference value labels also supplied their own native foreground. Keep legacy selectors and add the active
classes for enabled and disabled values and the popup arrow. The regression checks resolved themed foregrounds and popup-arrow tints across all six shipped themes in a hosted
panel, including disabled states. It waits for native color transitions to settle. Null reference labels keep muted text.

The label-toggle input fixture also depended on host assets having labels and on its target pane being visible. It now creates and deletes an owned labelled asset, preserves pane
preferences and opens the Objects pane from a compact Inspector-first state. Failure diagnostics include ancestor display/layout. A controlled compact case fails before pane
navigation; the corrected fixture passes in both native skins. A preliminary attempt to switch skins inside a running test applied too late and is excluded; actual measurements
start only after switching outside the runner and verifying the native skin. The original dark editor is restored and independently verified afterward.

[Final dark layout](layout-dark-2026-10-26.png) and [final light layout](layout-light-2026-10-26.png) use the existing DocsImageCapture fixture. Captures are inspected at original
resolution; temporary assets/preferences/selection and native hosts are restored. The read-only IMGUI drawer retains its native disabled appearance. These are scripted host checks,
not a human task-time/error study. Unity 2021.3 execution, representative other-platform/real-project evidence and the larger #306 matrix remain open.

## Validation scope

The full 932-case EditMode suite passed after the input-fixture repair, before the field-color repair. After the color repair, all 24 affected EditMode cases passed in each native
skin (48 executions), including the six-theme foreground regression. The final PlayMode suite passed all 15 cases. The repository gate passed all 16 checks. These bounded checks do
not claim a new full 933-case run after the stylesheet repair.
