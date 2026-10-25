# Keyboard label controls on 25 October 2026

Refs #312. The Labels arrow was a pointer-only Label. It is now a native Button routed through the owning window's input profile, with live shortcut hints and visible focus. Active
filters keep their controls open; clearing the filter enables hiding them again. The button keeps a 22-point square in all six shipped themes.

The hosted regression fails on the original control because no focusable button exists. It passes after the replacement, covering default submit, remapped and disabled keys,
profile replacement, remapped pointer buttons, current tooltip bindings, active-filter visibility and both persistence stores. Its six-theme geometry assertions pass. A preliminary
run caught the static tooltip overwriting the live hint; stopping immediate propagation repairs that case.

All 54 affected EditMode cases pass: mutation/refresh (22), input bindings (11), list selection (1), row layout (2) and label editor (18). All 15 actual PlayMode cases pass. The
parent source passed the complete 930-case EditMode suite; those full-suite results are not relabeled as this later source revision. Local gates pass.

[The 1180 × 780 capture](label-toggle-2026-10-25.png) uses the existing DocsImageCapture layout fixture on Unity 6000.5.2f1 Windows 11, dark editor skin and the Dx theme. It shows
representative temporary assets and the expanded Labels section. It was inspected at original dimensions. The capture restores preferences/selection/fixtures and closes its window.
It does not show a focused arrow, prove physical-keyboard usability, or establish light-editor or Unity 2021.3 execution. The broad exploratory usability and platform acceptance in
#312 remains open.
