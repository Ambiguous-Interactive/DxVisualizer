# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Add keyboard access to the Labels toggle, Settings switches and processor controls, with current input bindings, live hints and visible focus (#312, #488).
- Add editable input profiles for browser keys, modifiers, focus and pointer gestures. Apply profile changes to open controls and their shortcut hints (#312).
- Add Page Up/Down focus navigation to large catalogs, with viewport-sized jumps and unchanged selection. Enter rows with boundary/page keys after filtering clears focus (#324,
  #312).
- Add Move to top/bottom to asset Actions and Ctrl/Cmd+Home/End shortcuts for the selected asset, including offscreen rows (#312).
- Add viewport-bounded catalog rows with responsive headers. Preserve grouping, filters, order, selection, focus, gestures, full titles and exact type hints (#324, #312).
- Add atomic GUID selection import and copied export for headless editor tools, sharing browser selection intent and exact type identities (#315).
- Add paged C# and JSON discovery of browser-eligible types with exact identities, without opening a window or loading assets (#315, #316).
- Add shared C# and JSON previews and cooperative create, clone, rename, move and delete operations (#315, #316).
- Add shared C# and JSON data-folder and active-selection preferences. Configure persistence mode and themes from C# or JSON with state migration and idempotent writes (#315,
  #316).
- Add SELECTED processor scope with GUID selections, exact target previews, cancellable preparation and explicit batch failure outcomes (#317).
- Add named saved searches for query text, exact type scopes and label expressions, with cancellable results and user or project persistence. Use saved searches and validation
  inside the themed browser (#318).
- Add versioned JSON queries, validation and explicit processor preview/apply with paged responses, cancellation, and a Unity file entrypoint (#316).
- Add read-only validation of selected assets for missing Unity references and duplicate authored IDs, with cancellable progress and per-asset findings (#319).
- Add editor queries with exact type scopes and GUID pages. Preview explicit processor targets, cancel preparation, and inspect batch outcomes (#315).
- Add Show all results to reach matches beyond the first 25, with virtualized rows, keyboard navigation, and a return to quick search (#311).

### Fixed

- Fix delayed window layout replacing the preferred size with a monitor-clamped size. Keep later user resizes saved (#493).
- Fix dropdown and object-reference text in light editor skins. Keep field values and popup arrows readable across themes (#312).
- Fix catalog reorder and remove buttons changing the selected type or starting a drag (#312).
- Fix catalog arrow moves resetting after reopening the window. Preserve namespace and type order in user and project state (#324).
- Fix script-folder scans adding unsupported types. Add only eligible ScriptableObject types without creating instances (#376).
- Fix browser stalls during asset imports. Keep row updates responsive while preserving selection and saved order (#308).

### Changed

- Align asset-label entry and its Add button with consistent height and spacing across themes (#312).
- Yield selected-type asset loading between short work slices. Prioritize saved selection and keep custom order while remaining objects load (#309).
- Speed up namespace ordering, catalog scrolling and collapsed-group filtering while preserving saved positions and stable ties (#324).
- Keep repeated searches responsive during partial indexing by reusing prepared result order and search data across window rebuilds. Load assets only when activating search results
  without retaining every indexed asset (#309, #310).
- Search available asset text while indexing continues. Speed up Unicode queries, keep final ordering responsive, and refresh open searches as records arrive or change. Keep
  completed results stable when indexing adds no new search data (#310).
- Simplify all/any label filters with keyboard pills, Clear and Advanced modes. Keep missing active labels and focus on refresh, including inspector label actions (#314, #308,
  #312).
- Keep inspector pane controls and single-pane navigation usable at narrow widths. Restore saved widths and preserve asset-title hints on refresh or resize (#313, #308).
- Clarify Add types and search controls, group row commands in Actions, and add selected-asset shortcuts including Shift+F10 and Menu. Skip hidden catalog entries with arrows and
  collapse namespaces by keyboard. Use Home/End to focus catalog boundaries (#312).
- Reduce the vertical gap between object rows by at least half (#292).
- Point new installs and documentation links to Ambiguous-Interactive/DxVisualizer. Update site bookmarks to <https://ambiguous-interactive.github.io/DxVisualizer/>; the old Pages
  site does not redirect. Existing Git installs keep working (#151).

## [0.1.1] - 2026-10-18

### Added

- Add the DxVisualizer mark to the editor tab (#146).
- Publish a measured-performance page with the first recorded window-open, search, and selection timings, produced by a new reproducible measurement driver (#114).
- Add a captured global-search image to the search and filtering docs page, regenerated from the live window by the docs capture pipeline (#114).
- Show the asset-creation controls opening in the managing-assets guide.
- Add the Migration and Roadmap pages, and move the repository branding in-repo: the DxVisualizer mark, favicon, and banner from the rebrand kit now live under `docs/images/` and
  the site and README use them (#149, #150).

### Changed

- Renamed to DxVisualizer: display name, window title, and menu paths (**Tools → Wallstop Studios → DxVisualizer**). Namespaces, types, package name, saved state, and theme tokens
  are unchanged, so projects upgrade without edits (#147).
- The documentation site ships the designed Dx theme: ink dark as the default, nav tabs, the homepage hero, and screenshots recaptured from the live window in the Dx theme (#149,
  #152).
- The README leads with the banner and links into the site instead of repeating it (#149).

### Fixed

- Large asset import bursts no longer freeze the editor: the window decides which imports matter from each asset's type instead of loading every imported asset (#226).
- Window placement no longer overwrites your saved preferred window size with the size the operating system clamps the window to, so reopening keeps the size you chose (#39, #42).
- Fix the docs copy to match the window: a processor without an `Accepts` list is never offered, pane widths always save in editor preferences, and persistence docs use the
  on-screen **Persist State in Settings Asset** label. The settings tooltip now names the correct save location. (#153, #149)
- `BaseDataObject` asset ids no longer change on validation: an asset keeps its authored id, and only an empty id fills from the asset GUID. Created and cloned assets still get a
  fresh id (#137).

## [0.1.0] - 2026-10-07

### Added

- Compact and Minimal built-in style variants: density-only theme presets that keep the Classic palette and shrink window typography, action buttons, and control chrome (Compact
  13px type, 20px action buttons, 4px control radii; Minimal 12px type, 16px action buttons, square control corners).
- Theme hot reload: an open Data Visualizer window re-applies the selected theme immediately when its theme asset or referenced stylesheet is reimported, reference-swapped,
  renamed, moved, or deleted; deletions fall back to the package style with the existing saved-GUID semantics.
- A `.unitypackage` is now built and verified on each release and attached to the GitHub Release alongside the npm package.

### Changed

- Control typography and radii in the base stylesheet now resolve from the `--dataviz-font-size`, `--dataviz-circle-size`, and `--dataviz-control-radius` tokens instead of
  hardcoded values. The Classic preset keeps its type and glyph sizes; the three large round action buttons are 1px smaller to match the action-button token, and themes that
  override `--dataviz-font-size` now also restyle the standard prose controls.
