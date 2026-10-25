namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using System;
    using System.Collections.Generic;
    using Extensions;
    using Styles;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>A flattened catalog whose visual rows are bounded by the viewport.</summary>
    public sealed class VirtualizedTypeCatalogView : VisualElement
    {
        public ListView List { get; }
        public CatalogInputSettings Input { get; }
        public int VisibleNamespaceCount { get; private set; }

        private readonly DataVisualizer _owner;
        private readonly NamespaceController _controller;
        private readonly Dictionary<string, List<Type>> _managed;
        private readonly Dictionary<string, int> _order;
        private readonly List<CatalogEntry> _rows = new();
        private readonly List<CatalogEntry> _catalogRows = new();
        private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
        private readonly List<(int Start, int Length)> _terms = new();
        private int _filterStart;
        private int _filterLength;
        private string _filterText = string.Empty;
        private Type _selected;
        private CatalogEntry? _focusedEntry;
        private string _focusedControl;
        private CatalogEntry? _pressed;
        private Vector2 _pressPosition;
        private int _pressedPointerId;
        private bool _dragging;
        private int _generation;

        public VirtualizedTypeCatalogView(
            DataVisualizer owner,
            NamespaceController controller,
            Dictionary<string, List<Type>> managed,
            Dictionary<string, int> order,
            CatalogInputSettings input = null
        )
        {
            Input = input ?? new CatalogInputSettings();
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            _owner = owner;
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _managed = managed ?? throw new ArgumentNullException(nameof(managed));
            _order = order ?? throw new ArgumentNullException(nameof(order));
            name = "virtualized-type-catalog";
            focusable = true;
            RegisterCallback<TooltipEvent>(evt =>
            {
                evt.tooltip =
                    $"{Input.FirstRowKey}/{Input.LastRowKey}: focus first/last visible row. Namespace {Input.PreviousTypeKey}/{Input.NextTypeKey}: move focus. {Input.SelectKey}/{Input.ToggleKey}: select or toggle.";
                evt.rect = worldBound;
                evt.StopPropagation();
            });
            AddToClassList(StyleConstants.VirtualCatalogClass);
            foreach (string key in managed.Keys)
            {
                if (NamespaceController.IsNamespaceCollapsed(owner, key))
                {
                    _collapsed.Add(key);
                }
            }
            List = new ListView
            {
                name = "catalog-list",
                fixedItemHeight = 40,
                selectionType = SelectionType.None,
                itemsSource = _rows,
                makeItem = MakeRow,
                bindItem = BindRow,
            };
            List.style.flexGrow = 1;
            Add(List);
            RegisterCallback<FocusOutEvent>(evt =>
            {
                if (evt.relatedTarget is VisualElement next && !Contains(next))
                {
                    _focusedEntry = null;
                }
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                ++_generation;
                _pressed = null;
                _dragging = false;
            });
            Reconcile();
        }

        public bool ContainsType(Type type)
        {
            return type != null && _managed.TryGetValue(NamespaceController.GetNamespaceKey(type), out List<Type> types) && types.Contains(type);
        }

        public bool Filter(string text)
        {
            string source = text ?? string.Empty;
            int start = 0;
            int end = source.Length;
            while (start < end && char.IsWhiteSpace(source[start]))
            {
                start++;
            }
            while (start < end && char.IsWhiteSpace(source[end - 1]))
            {
                end--;
            }
            if (_filterText.AsSpan(_filterStart, _filterLength).Equals(source.AsSpan(start, end - start), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            _filterText = source;
            _filterStart = start;
            _filterLength = end - start;
            _terms.Clear();
            for (int index = start; index < end; )
            {
                if (source[index] == ' ')
                {
                    index++;
                    continue;
                }
                int termStart = index++;
                while (index < end && source[index] != ' ')
                {
                    index++;
                }
                _terms.Add((termStart, index - termStart));
            }
            Refresh();
            return true;
        }

        public void Select(Type type, bool reveal = true)
        {
            bool changed = _selected != type;
            bool moveFocus = changed && _focusedEntry.HasValue && type != null;
            if (moveFocus)
            {
                _focusedEntry = new CatalogEntry(NamespaceController.GetNamespaceKey(type), type);
                _focusedControl = null;
            }
            _selected = type;
            bool expanded = reveal && type != null && _collapsed.Remove(NamespaceController.GetNamespaceKey(type));
            if (expanded)
            {
                Refresh();
            }
            else
            {
                if (changed)
                {
                    ++_generation;
                }
                foreach (CatalogRow row in List.Query<CatalogRow>().Build())
                {
                    if (row.userData is CatalogEntry entry)
                    {
                        UpdateRowPresentation(row, entry);
                    }
                }
                if (reveal && moveFocus)
                {
                    RestoreCatalogFocus(_focusedEntry, _focusedControl, _generation);
                }
            }
            if (reveal && type != null)
            {
                int index = Find(type);
                if (0 <= index)
                {
                    List.ScrollToItem(index);
                }
            }
        }

        public Type Adjacent(Type current, bool forward)
        {
            if (
                current == null
                || !_managed.TryGetValue(NamespaceController.GetNamespaceKey(current), out List<Type> types)
                || _collapsed.Contains(NamespaceController.GetNamespaceKey(current))
            )
            {
                return null;
            }
            int index = types.IndexOf(current);
            if (index < 0)
            {
                return null;
            }
            string key = NamespaceController.GetNamespaceKey(current);
            int visible = _rows.IndexOf(new CatalogEntry(key, null)) + 1;
            if (visible == 0)
            {
                return null;
            }
            Type first = null;
            Type last = null;
            Type previous = null;
            Type next = null;
            bool passedCurrent = false;
            foreach (Type candidate in types)
            {
                bool isCurrent = candidate == current;
                passedCurrent = passedCurrent || isCurrent;
                if (_rows.Count <= visible || _rows[visible].Type != candidate || !string.Equals(_rows[visible].NamespaceKey, key, StringComparison.Ordinal))
                {
                    continue;
                }
                visible++;
                first ??= candidate;
                last = candidate;
                if (!isCurrent)
                {
                    if (passedCurrent)
                    {
                        next ??= candidate;
                    }
                    else
                    {
                        previous = candidate;
                    }
                }
            }
            return forward ? next ?? first : previous ?? last;
        }

        /// <summary>Rebuilds rows only when the complete ordered catalog changes.</summary>
        public bool Reconcile()
        {
            KeyValuePair<string, List<Type>>[] groups = NamespaceController.GetOrderedNamespaceEntries(_managed, _order);
            int count = 0;
            bool changed = false;
            foreach (KeyValuePair<string, List<Type>> group in groups)
            {
                changed = CheckEntry(group.Key, null, count++) || changed;
                foreach (Type type in group.Value)
                {
                    changed = CheckEntry(group.Key, type, count++) || changed;
                }
            }
            if (!changed && count == _catalogRows.Count)
            {
                return false;
            }
            _catalogRows.Clear();
            foreach (KeyValuePair<string, List<Type>> group in groups)
            {
                _catalogRows.Add(new CatalogEntry(group.Key, null));
                foreach (Type type in group.Value)
                {
                    _catalogRows.Add(new CatalogEntry(group.Key, type));
                }
            }
            _pressed = null;
            _dragging = false;
            Refresh();
            return true;
        }

        public void Refresh()
        {
            CatalogEntry? focused = _focusedEntry;
            string control = _focusedControl;
            foreach (CatalogRow row in List.Query<CatalogRow>().Build())
            {
                if (row.HasPointerCapture(_pressedPointerId))
                {
                    row.ReleasePointer(_pressedPointerId);
                }
            }
            _pressed = null;
            _dragging = false;
            RebuildVisibleRows();
            int generation = ++_generation;
            List.RefreshItems();
            RestoreCatalogFocus(focused, control, generation);
        }

        public bool Drop(CatalogEntry source, CatalogEntry target, bool after)
        {
            if (
                _owner.IsAssetEditingSuspended
                || source.Equals(target)
                || source.NamespaceKey == null
                || target.NamespaceKey == null
                || !_managed.ContainsKey(source.NamespaceKey)
                || !_managed.ContainsKey(target.NamespaceKey)
            )
            {
                return false;
            }
            if (source.Type == null && target.Type == null)
            {
                List<string> keys = new(_managed.Count);
                foreach (KeyValuePair<string, List<Type>> group in NamespaceController.GetOrderedNamespaceEntries(_managed, _order))
                {
                    keys.Add(group.Key);
                }
                int from = keys.IndexOf(source.NamespaceKey);
                int targetIndex = keys.IndexOf(target.NamespaceKey);
                if (from < 0 || targetIndex < 0)
                {
                    return false;
                }
                int destinationIndex = targetIndex + (after ? 1 : 0);
                if (from < destinationIndex)
                {
                    destinationIndex--;
                }
                if (from == destinationIndex)
                {
                    return false;
                }
                keys.RemoveAt(from);
                keys.Insert(destinationIndex, source.NamespaceKey);
                for (int i = 0; i < keys.Count; i++)
                {
                    _order[keys[i]] = i;
                }
                _owner.PersistVirtualCatalogOrder(null, keys);
            }
            else if (
                source.Type != null
                && target.Type != null
                && string.Equals(source.NamespaceKey, target.NamespaceKey, StringComparison.Ordinal)
                && _managed.TryGetValue(source.NamespaceKey, out List<Type> types)
            )
            {
                int from = types.IndexOf(source.Type);
                int to = types.IndexOf(target.Type);
                if (from < 0 || to < 0)
                {
                    return false;
                }
                int destinationIndex = to + (after ? 1 : 0);
                if (from < destinationIndex)
                {
                    destinationIndex--;
                }
                if (from == destinationIndex)
                {
                    return false;
                }
                types.RemoveAt(from);
                types.Insert(destinationIndex, source.Type);
                _owner.PersistVirtualCatalogOrder(source.NamespaceKey, null);
            }
            else
            {
                return false;
            }
            Reconcile();
            return true;
        }

        private void RebuildVisibleRows()
        {
            _rows.Clear();
            VisibleNamespaceCount = 0;
            // Reconcile owns canonical ordering; filtering and collapse only project that snapshot.
            for (int index = 0; index < _catalogRows.Count; )
            {
                CatalogEntry group = _catalogRows[index++];
                int header = _rows.Count;
                _rows.Add(group);
                bool expanded = !_collapsed.Contains(group.NamespaceKey);
                bool matches = false;
                while (index < _catalogRows.Count && !_catalogRows[index].IsNamespace)
                {
                    CatalogEntry entry = _catalogRows[index++];
                    if (MatchesFilter(entry.Type))
                    {
                        matches = true;
                        if (expanded)
                        {
                            _rows.Add(entry);
                        }
                    }
                }
                if (!matches && _terms.Count != 0)
                {
                    _rows.RemoveAt(header);
                }
                else
                {
                    VisibleNamespaceCount++;
                }
            }
        }

        private void RestoreCatalogFocus(CatalogEntry? focused, string control, int generation)
        {
            if (focused.HasValue)
            {
                CatalogEntry identity = focused.Value;
                int index = _rows.IndexOf(identity);
                if (0 <= index)
                {
                    List.ScrollToItem(index);
                    int attempts = 0;
                    bool restored = false;
                    schedule
                        .Execute(() =>
                        {
                            attempts++;
                            if (generation == _generation && panel != null)
                            {
                                VisualElement row = FindRealized(identity);
                                VisualElement target = string.IsNullOrEmpty(control) ? row : row?.Q<VisualElement>(control) ?? row;
                                if (target != null)
                                {
                                    target.Focus();
                                    restored = true;
                                }
                            }
                        })
                        .Every(16)
                        .Until(() => restored || generation != _generation || panel == null || 8 <= attempts);
                }
                else
                {
                    Focus();
                }
            }
        }

        private void FocusVisibleRow(int index)
        {
            if (index < 0 || _rows.Count <= index)
            {
                return;
            }
            _focusedEntry = _rows[index];
            _focusedControl = null;
            RestoreCatalogFocus(_focusedEntry, null, ++_generation);
        }

        private bool MatchesFilter(Type type)
        {
            if (_terms.Count == 0)
            {
                return true;
            }
            string title = NamespaceController.GetTypeDisplayName(type);
            foreach ((int Start, int Length) term in _terms)
            {
                if (title.AsSpan().IndexOf(_filterText.AsSpan(term.Start, term.Length), StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }
            return true;
        }

        private bool CheckEntry(string key, Type type, int index)
        {
            return _catalogRows.Count <= index || _catalogRows[index].Type != type || !string.Equals(_catalogRows[index].NamespaceKey, key, StringComparison.Ordinal);
        }

        private bool IsSelected(CatalogEntry entry)
        {
            return _selected != null
                && (entry.Type == null ? string.Equals(entry.NamespaceKey, NamespaceController.GetNamespaceKey(_selected), StringComparison.Ordinal) : entry.Type == _selected);
        }

        private int Find(Type type)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Type == type && type != null)
                {
                    return i;
                }
            }
            return -1;
        }

        private VisualElement FindRealized(CatalogEntry identity)
        {
            foreach (VisualElement row in List.Query<VisualElement>(className: StyleConstants.VirtualCatalogRowClass).Build())
            {
                if (row.userData is CatalogEntry entry && entry.Equals(identity))
                {
                    return row;
                }
            }
            return null;
        }

        private void UpdateRowPresentation(CatalogRow row, CatalogEntry entry)
        {
            row.EnableInClassList(StyleConstants.SelectedClass, IsSelected(entry));
            bool canEdit = !_owner.IsAssetEditingSuspended;
            row.MoveUpButton.SetEnabled(canEdit && row.CanMoveUp);
            row.MoveDownButton.SetEnabled(canEdit && row.CanMoveDown);
        }

        private VisualElement MakeRow()
        {
            CatalogRow row = new() { focusable = true };
            row.AddToClassList(StyleConstants.VirtualCatalogRowClass);
            Button collapse = new(() => Toggle((CatalogEntry)row.userData)) { name = "catalog-collapse" };
            collapse.AddToClassList(StyleConstants.NamespaceIndicatorClass);
            row.Add(collapse);
            Button up = new(() => Move((CatalogEntry)row.userData, true)) { name = "go-up-button", text = "↑" };
            Button down = new(() => Move((CatalogEntry)row.userData, false)) { name = "go-down-button", text = "↓" };
            row.MoveUpButton = up;
            row.MoveDownButton = down;
            row.Add(up);
            row.Add(down);
            Label label = new() { name = "catalog-label" };
            label.AddToClassList(StyleConstants.TypeLabelClass);
            row.Add(label);
            Button remove = new(() => Remove((CatalogEntry)row.userData, row)) { name = "catalog-remove", text = "X" };
            remove.AddToClassList(StyleConstants.DeleteButtonClass);
            row.Add(remove);
            foreach (Button button in new[] { collapse, up, down, remove })
            {
                button.AddToClassList(StyleConstants.ActionButtonClass);
                button.RegisterCallback<PointerDownEvent>(static evt => evt.StopPropagation());
                button.RegisterCallback<PointerUpEvent>(static evt => evt.StopPropagation());
            }
            row.RegisterCallback<FocusInEvent>(evt =>
            {
                _focusedEntry = (CatalogEntry)row.userData;
                _focusedControl = (evt.target as VisualElement)?.name;
            });
            row.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (_owner.IsAssetEditingSuspended)
                {
                    return;
                }
                CatalogEntry entry = (CatalogEntry)row.userData;
                if (evt.keyCode == Input.CancelDragKey && _pressed.HasValue)
                {
                    row.ReleasePointer(_pressedPointerId);
                }
                else if (entry.IsNamespace && (evt.keyCode == Input.PreviousTypeKey || evt.keyCode == Input.NextTypeKey))
                {
                    FocusVisibleRow(_rows.IndexOf(entry) + (evt.keyCode == Input.NextTypeKey ? 1 : -1));
                }
                else if (entry.Type != null && (evt.keyCode == Input.PreviousTypeKey || evt.keyCode == Input.NextTypeKey))
                {
                    Type next = Adjacent(entry.Type, evt.keyCode == Input.NextTypeKey);
                    if (next != null)
                    {
                        _controller.SelectType(_owner, next);
                    }
                }
                else if (entry.Type == null && (evt.keyCode == Input.CollapseKey || evt.keyCode == Input.ExpandKey))
                {
                    SetCollapsed(entry.NamespaceKey, evt.keyCode == Input.CollapseKey);
                }
                else if (Input.IsSelectionKey(evt.keyCode))
                {
                    if (evt.target != row)
                    {
                        return;
                    }
                    Activate(entry);
                }
                else if (evt.keyCode == Input.FirstRowKey || evt.keyCode == Input.LastRowKey)
                {
                    FocusVisibleRow(evt.keyCode == Input.FirstRowKey ? 0 : _rows.Count - 1);
                }
                else
                {
                    return;
                }
                evt.StopPropagation();
                evt.PreventDefaultCompat();
            });
            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == Input.PointerButton && !_owner.IsAssetEditingSuspended)
                {
                    row.Focus();
                    _pressed = (CatalogEntry)row.userData;
                    _pressPosition = evt.position;
                    _pressedPointerId = evt.pointerId;
                    _dragging = false;
                    row.CapturePointer(evt.pointerId);
                    evt.StopPropagation();
                }
            });
            row.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_pressed.HasValue && row.HasPointerCapture(evt.pointerId))
                {
                    _dragging = _dragging || Input.HasPassedDragThreshold(_pressPosition, evt.position);
                    row.EnableInClassList(StyleConstants.SelectedClass, _dragging || IsSelected((CatalogEntry)row.userData));
                }
            });
            row.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!_pressed.HasValue || !row.HasPointerCapture(evt.pointerId))
                {
                    return;
                }
                CatalogEntry source = _pressed.Value;
                bool dragging = _dragging;
                _pressed = null;
                _dragging = false;
                row.ReleasePointer(evt.pointerId);
                if (!_owner.IsAssetEditingSuspended)
                {
                    if (dragging)
                    {
                        VisualElement target = panel.Pick(evt.position);
                        while (target != null && target.userData is not CatalogEntry)
                        {
                            target = target.parent;
                        }
                        if (target?.userData is CatalogEntry destination)
                        {
                            Drop(source, destination, target.worldBound.center.y < evt.position.y);
                        }
                    }
                    else
                    {
                        Activate(source);
                    }
                }
                evt.StopPropagation();
            });
            row.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                _pressed = null;
                _dragging = false;
                row.EnableInClassList(StyleConstants.SelectedClass, IsSelected((CatalogEntry)row.userData));
            });
            VisualElement slot = new();
            slot.style.height = 40;
            slot.Add(row);
            return slot;
        }

        private void BindRow(VisualElement slot, int index)
        {
            CatalogRow row = (CatalogRow)slot[0];
            CatalogEntry entry = _rows[index];
            if (row.userData is CatalogEntry previous && !previous.Equals(entry) && row.HasPointerCapture(_pressedPointerId))
            {
                row.ReleasePointer(_pressedPointerId);
            }
            if (!_managed.TryGetValue(entry.NamespaceKey, out List<Type> types))
            {
                row.style.display = DisplayStyle.None;
                return;
            }
            row.style.display = DisplayStyle.Flex;
            row.userData = entry;
            bool header = entry.Type == null;
            string title = header ? entry.NamespaceKey : NamespaceController.GetTypeDisplayName(entry.Type);
            row.name = header ? "namespace-header-" + entry.NamespaceKey : "type-item-" + entry.Type.Name;
            row.EnableInClassList(StyleConstants.TypeItemClass, !header);
            row.EnableInClassList(StyleConstants.NamespaceHeaderClass, header);
            Label label = row.Q<Label>("catalog-label");
            label.text = title;
            label.tooltip = header ? entry.NamespaceKey : entry.Type.FullName;
            label.EnableInClassList(StyleConstants.BoldClass, header);
            Button collapse = row.Q<Button>("catalog-collapse");
            collapse.style.display = header ? DisplayStyle.Flex : DisplayStyle.None;
            collapse.text = _collapsed.Contains(entry.NamespaceKey) ? StyleConstants.ArrowCollapsed : StyleConstants.ArrowExpanded;
            collapse.tooltip = $"Collapse or expand {title} ({Input.SelectKey}/{Input.ToggleKey}; {Input.CollapseKey}/{Input.ExpandKey})";
            Button up = row.Q<Button>("go-up-button");
            Button down = row.Q<Button>("go-down-button");
            up.tooltip = "Move " + title + " to top";
            down.tooltip = "Move " + title + " to bottom";
            row.CanMoveUp = header ? !string.Equals(entry.NamespaceKey, _catalogRows[0].NamespaceKey, StringComparison.Ordinal) : entry.Type != types[0];
            row.CanMoveDown = header
                ? !string.Equals(entry.NamespaceKey, _catalogRows[_catalogRows.Count - 1].NamespaceKey, StringComparison.Ordinal)
                : entry.Type != types[types.Count - 1];
            UpdateRowPresentation(row, entry);
            Button remove = row.Q<Button>("catalog-remove");
            int removable = 0;
            if (header)
            {
                foreach (Type type in types)
                {
                    if (NamespaceController.IsTypeRemovable(type))
                    {
                        removable++;
                        if (1 < removable)
                        {
                            break;
                        }
                    }
                }
            }
            remove.style.display = header
                ? (1 < removable ? DisplayStyle.Flex : DisplayStyle.None)
                : (NamespaceController.IsTypeRemovable(entry.Type) ? DisplayStyle.Flex : DisplayStyle.None);
            remove.tooltip = "Remove " + title + " from DxVisualizer";
        }

        private void Activate(CatalogEntry entry)
        {
            if (entry.Type == null)
            {
                Toggle(entry);
            }
            else
            {
                _controller.SelectType(_owner, entry.Type);
            }
        }

        private void Toggle(CatalogEntry entry)
        {
            if (!_owner.IsAssetEditingSuspended)
            {
                SetCollapsed(entry.NamespaceKey, !_collapsed.Contains(entry.NamespaceKey));
            }
        }

        private void SetCollapsed(string key, bool collapsed)
        {
            if (collapsed)
            {
                _collapsed.Add(key);
            }
            else
            {
                _collapsed.Remove(key);
            }
            _owner.PersistSettings(settings => settings.SetNamespaceCollapsed(key, collapsed), state => state.SetNamespaceCollapsed(key, collapsed));
            Refresh();
        }

        private void Remove(CatalogEntry entry, VisualElement anchor)
        {
            if (_owner.IsAssetEditingSuspended)
            {
                return;
            }
            if (entry.Type == null)
            {
                List<Type> removable = new();
                foreach (Type type in _managed[entry.NamespaceKey])
                {
                    if (NamespaceController.IsTypeRemovable(type))
                    {
                        removable.Add(type);
                    }
                }
                _owner.BuildAndOpenConfirmationPopover(
                    "Remove " + removable.Count + " non-core types from '" + entry.NamespaceKey + "'?",
                    "Remove",
                    () => _controller.RemoveCatalogNamespaceTypes(_owner, entry.NamespaceKey, removable),
                    anchor
                );
                return;
            }
            _owner.BuildAndOpenConfirmationPopover(
                "Remove type '" + entry.Type.Name + "' from DxVisualizer?",
                "Remove",
                () => _controller.RemoveCatalogType(_owner, entry.Type),
                anchor
            );
        }

        private void Move(CatalogEntry entry, bool top)
        {
            if (_owner.IsAssetEditingSuspended || !_managed.TryGetValue(entry.NamespaceKey, out List<Type> types))
            {
                return;
            }
            CatalogEntry destination =
                entry.Type == null
                    ? new CatalogEntry(NamespaceController.GetOrderedNamespaceEntries(_managed, _order)[top ? 0 : _managed.Count - 1].Key, null)
                    : new CatalogEntry(entry.NamespaceKey, types[top ? 0 : types.Count - 1]);
            Drop(entry, destination, !top);
        }

        private sealed class CatalogRow : VisualElement
        {
            public Button MoveUpButton { get; set; }
            public Button MoveDownButton { get; set; }
            public bool CanMoveUp { get; set; }
            public bool CanMoveDown { get; set; }
        }
    }
}
