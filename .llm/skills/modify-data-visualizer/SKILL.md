---
name: modify-data-visualizer
description: Navigate and modify the DxVisualizer window codebase safely - file responsibility map, persistence models, async batch loading, search, and styling. Use when changing window behavior, fixing bugs, or refactoring editor code.
metadata:
  category: Feature
---

# Skill: Modify DxVisualizer

## Navigation Map

| Path | Responsibility |
| --- | --- |
| `Editor/DataVisualizer/DataVisualizer.cs` | Main `EditorWindow`; ~9.3k lines - always search (`rg`) before editing |
| `Editor/DataVisualizer/NamespaceController.cs` | Namespace/type catalog, drag-drop ordering |
| `Editor/DataVisualizer/Data/` | Persisted models: `DataVisualizerUserState`, `DataVisualizerSettings`, `TypeObjectOrder`, `NamespaceTypeOrder`, `NamespaceCollapseState`, `LastObjectSelectionEntry`, `TypeLabelFilterConfig`, `ProcessorState`, plus pure logic (`LabelFilterEvaluator`) |
| `Editor/DataVisualizer/Search/` | Global search matching: `MatchSource`, `MatchDetail`, `SearchResultMatchInfo` |
| `Editor/DataVisualizer/UI/` | Reusable controls: `HorizontalToggle`, `ActionButtonToggle` |
| `Editor/DataVisualizer/Styles/` | `StyleConstants.cs` + `DataVisualizerStyles.uss` |
| `Editor/DataVisualizer/Unity/` | `DataVisualizerAssetPostprocessor` |
| `Editor/DataVisualizer/Utilities/` | `ReadOnlyAttribute` + drawer, `MonitorUtility` |

## Working in the Main Window File

- Name managed types in PascalCase even when wrapping all-caps native typedefs. Add
  a descriptive prefix such as `Native` when the idiomatic name would shadow a Unity
  or framework type, and keep the filename identical to the type name.
- `rg` for the feature first (constants, method names); the file predates the split,
  so new subsystems should go into `Data/`, `Search/`, `UI/`, or `Utilities/` files
  instead of growing `DataVisualizer.cs` further.
- Async asset loading runs in batches (`AsyncLoadBatchSize`,
  `AsyncLoadPriorityBatchSize` constants) - preserve batch invariants when touching
  load paths and keep debug logging behind `EnableAsyncLoadDebugLog`.
- Selection persistence is regression-prone; changes to selection or ordering need a
  `SelectionPersistenceTests`-style EditMode test.
- Use indented block comments for explanations spanning multiple lines; reserve `//`
  for one-line notes and tool directives, and retain `///` for XML documentation.
  Write comments in Simplified Technical English: short, plain sentences, no filler.

## Input Routing

- Every package-owned navigation/action key, modifier and pointer gesture uses
  `DataVisualizer.Input`, backed by the Settings input-profile asset. Standalone
  controls accept `Func<DataVisualizerInputSettings>` and own independent defaults;
  never create a mutable static default or cache a replaced profile.
- Defaults belong in `CatalogInputSettings`/`DataVisualizerInputSettings`.
  `NativeInputRouter` alone translates to Unity control gestures. Do not add
  physical key/button literals to handlers. Native OS menus and text editing keep
  their platform behavior.
- Test remaps through the hosted control, not just the binding matcher: new keys
  work, old keys stop working, None is unbound, and modifiers are exact. Keep
  catalog selection/navigation precedence when bindings overlap.
- Intercept native controls from their owning ancestor before native callbacks.
  Consume the backing IMGUI event as well as UI Toolkit propagation when relaying
  a gesture; otherwise an old native key can still move selection. Key-relay guards
  are owner-local and disposed through a named scope. Pointer dispatch may queue:
  retain forwarded-event identity until its callback runs, then remove it. Clear
  pending identities on every owner removal path; a call-scoped flag is too short.
  Capture row identity and panel with a queued pointer and cancel its dispatch if
  the target is recycled, destroyed or moved to another panel before delivery.
- Use Unity's public focus ring for configured focus traversal. Native key relays
  supply the matching character as well as the key code; test both directions from
  a text input so focus keys cannot enter text or leave backward traversal broken.
- Read shortcut hints from current bindings when the tooltip/menu opens. Replacing
  or editing a profile must not need row recreation.

## Persistence Safety

- User state is JSON (`DataVisualizerUserState.json`) or a settings asset depending
  on the "persist state in user settings" toggle; both paths must stay consistent
  (see the dirty-state fix history).
- Renaming serialized fields or JSON keys requires `[FormerlySerializedAs]` or an
  explicit migration; never orphan saved keys silently.
- EditorPrefs keys share the `PrefsPrefix` const; reuse the prefix for new keys.

- When adding a persisted settings field, update `PersistenceContractTests` deliberately
  and preserve defaults for old assets. Input profiles are a project configuration
  reference; they do not become per-user selection state. Route assignments through
  `EditorConfigurationService`, including read-only checks, targeted saves and typed
  failures; keep the JSON configuration API and schemas aligned with the UI.

## Asset Discovery

- Treat `AssetDatabase.FindAssets` as an I/O boundary. Call it once per logical
  type refresh unless a measured requirement justifies another query, and reuse its
  returned array when no merge is needed.
- Search folders constrain a query; they cannot express "this type anywhere OR any
  asset in this folder." Do not simulate that union with repeated searches in a
  per-type loop.
- Unity type filters can omit editor-only `ScriptableObject` assets whose class has
  no matching `MonoScript`. Persist exact asset GUIDs at create, clone, reorder, and
  move boundaries, remove them on delete, then validate and merge those direct GUIDs
  with the single type query.
- Validate direct GUIDs with `GetMainAssetTypeAtPath` and exact type equality. Do not
  instantiate arbitrary user types to probe script metadata or broaden a per-type
  refresh into a synchronous project-wide asset scan.
- While the AssetDatabase is busy (`AssetGuidDiscovery.IsAssetDatabaseBusy()`), skip GUID
  normalization: `MergeCandidates` keeps referenced/saved order GUIDs as raw unverified
  candidates and hands the raw saved GUID back, and the window re-normalizes saved
  selections once the database settles instead of clearing them during the transient
  post-recompile window that bursts Unity's missing-script warnings.
- Never-registered assets missed by Unity's type filters come from the shared lazy
  `AssetGuidTypeIndex`. It snapshots project `.asset` paths after first use, classifies
  them cooperatively through `GetMainAssetTypeAtPath`, and refreshes the window after
  completion. Keep this index exact-type, load-free, shared across managed types, and
  maintained by the asset postprocessor rather than adding another fallback scan.
- `AssetGuidTypeIndex` is an instantiable sealed service; production coordinates through
  its explicit `Shared` instance while tests may create isolated instances. Keep its
  state changes and editor-update subscription ownership in the instance state-machine
  runner/transition method. AssetDatabase paths are project-relative and use forward
  slashes on every Unity editor platform; preserve that canonical form for comparisons.
- Discovery is intentionally limited to exact main assets. Derived instances and
  subassets do not satisfy a selected base/main type; keep this boundary covered by
  tests instead of broadening equality to assignability.

## Async Ordering

- `_asyncDisplayOrderByGuid` is the full canonical order, including unloaded GUIDs;
  `_selectedObjects` is only the currently loaded subset. Never serialize the subset
  directly or append pending GUIDs after it.
- During a partial load, reorder loaded GUIDs within their canonical slots so
  unloaded entries retain position. For explicit create, clone, top, bottom, or
  delete operations, mutate the full canonical GUID order first, then merge and
  persist the loaded view.
- Keep `_asyncDisplayOrderByGuid` and `_selectedObjectOrderIndex` synchronized after
  every canonical-order mutation; pending batches use those indexes for insertion.
- Check active-list membership before a delayed row action changes canonical order,
  persisted state or focus. Reorder and clone callbacks can outlive type/filter
  changes. Explicit create/clone insertion must keep other active types out of the
  saved order. Exercise stale callbacks during partial loading and both stores.
- GUID order is stable and persisted. Never use swap-back removal in these lists.
  A move performs one `RemoveAt` plus one insertion; do not replace it with
  `RemoveAll`, which adds a predicate scan and discards the original index.
- Order mutators operate on the production-owned `List<string>`; read-only merge
  inputs remain `IReadOnlyList<string>`. Benchmark actual call-site shapes before
  introducing array/interface overloads rather than adding unused specializations.

## Filtering and Search

- Label filtering is config-driven (`TypeLabelFilterConfig` with AND/OR combination
  via `LabelCombinationType`) and evaluated by the pure `LabelFilterEvaluator` -
  extend the evaluator, not the UI, when adding filter semantics.
- Search results are aggregated into `SearchResultMatchInfo` with per-source
  `MatchSource`/`MatchDetail` entries; cap visible results (`MaxSearchResults`).
- Search-result objects are per-candidate and normally rendered once. Matched-term
  aggregation is bounded by the user's query tokens, so find first occurrences with a
  direct scan instead of attaching caches or introducing a pooled scratch set. Keep
  lazy enumerations independent and cover interleaved and early-disposal behavior.
- Classify other temporary sets by cardinality, call frequency, comparer, lifetime,
  concurrency, and retained capacity before optimizing them. Keep unbounded GUID/type
  membership sets local to their operation unless profiling proves repeated allocation
  is material. If pooling is proven, define reset and retention semantics first and
  generalize only when at least two call sites share those semantics.

## Collection Scans

- Default to hand-rolled `foreach` first-match scans. Measured on the Unity
  6000.4.6f1 Mono editor (PR #119 benchmark): at N=5, `foreach` runs in 77 ns with
  zero allocation while `Array.Exists` with a capturing lambda takes 154 ns (2x)
  and `Array.Exists` with a static method group takes 326 ns (4x); the lambda
  shapes allocate a closure plus delegate per call (~108 B/call measured via
  `List.FindIndex` at N=1000). Rule 33 says the same for bounded small inputs.
- Static helpers (`Array.Exists`, `List<T>.Exists`/`.Find`/`.FindIndex`) are not
  forbidden - they are plain `System.Array`/`List<T>` members, legal even in the
  `System.Linq`-restricted files (rule 32) - but treat them as a measured
  optimization, not a default. The one shape that beat `foreach` on time was
  `List<T>.Exists`/`FindIndex` at N=1000 (internal indexed loop vs struct
  enumerator, ~19% faster), still paying the per-call delegate allocation;
  only consider that trade on cold paths with genuinely large collections.
- Do not assume method groups are cached or allocation-free on Unity's C# 9
  compiler; measure before converting. Do not "simplify" an existing allocation-free
  scan into a helper form without data - reviewer-enforced (PR #119): the owner
  benchmarked an `Array.Find` suggestion and had it reverted.

## Styling

- Add USS classes to `DataVisualizerStyles.uss` and expose names through
  `StyleConstants`; the window applies stylesheets from `Styles/`.

## Related Skills

- [create-editor-window](./create-editor-window/SKILL.md) - window conventions
- [extend-runtime-api](./extend-runtime-api/SKILL.md) - runtime hooks the window calls
- [create-editmode-test](./create-editmode-test/SKILL.md) - test patterns for the
  pure-logic files
