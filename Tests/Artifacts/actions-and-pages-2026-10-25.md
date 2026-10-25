# Asset ordering and catalog pages on 25 October 2026

Refs #312 and #324. The source changes are in native stack #462: selected-asset ordering (#460), catalog page focus (#461), and refresh type relevance (#463).

## Rendered layouts

- [1180 × 780 window](actions-pages-normal-2026-10-25.png): catalog, asset Actions and inspector.
- [760 × 780 window](actions-pages-compact-2026-10-25.png): compact pane navigation with the selected inspector visible.

Both use the existing DocsImageCapture CaptureFrame layout fixture on Unity 6000.5.2f1, Windows 11, the dark editor and the Dx theme. The captures contain temporary representative
assets and are inspected at their original dimensions. The capture helper restores preferences, selection and owned fixtures, closes its window and cancels its index work. These
are combined-stack layout captures, not native Actions-menu paint, physical-keyboard usability, light-editor or Unity 2021.3 evidence. Existing public layout images remain
applicable because the added commands and keys do not change the row structure.

## Behavior validation

- Four selected-ordering cases pass across Control/Command and user/project persistence. They cover offscreen selection, saved order, hidden-row membership and direct/shortcut Play
  guards.
- The extended 2,000-type hosted catalog regression fails on the previous renderer at the Page Up destination. It passes on the new renderer across both distributions and
  persistence stores, with filtered boundaries, recycling, configurable keys and unchanged selection. Seven input-setting cases also pass.
- The final combined C# tree passes all 903 EditMode cases (759.16 seconds) and 15 actual PlayMode cases, with no failures/skips. The PlayMode positive control asserts
  Application.isPlaying.
- The 16-step local gate and Linux/Windows/documentation CI pass. The actual 269-file npm tarball matches allowed source paths and bytes and excludes test evidence.
- [Refresh relevance measurements](refresh-relevance-2026-10-25.md) retain their separate source/module identities and limits.

The full parent issues remain open. These checks do not complete the exploratory before/after add/find/clone/rename/move/delete study, real 100k-asset measurements, Unity 2021.3
execution, or the remaining light-editor/platform matrix.
