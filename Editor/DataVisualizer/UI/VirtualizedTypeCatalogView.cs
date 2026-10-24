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
        public int VisibleNamespaceCount { get; private set; }

        private readonly DataVisualizer _owner;
        private readonly NamespaceController _controller;
        private readonly Dictionary<string, List<Type>> _managed;
        private readonly Dictionary<string, int> _order;
        private readonly List<KeyValuePair<string, Type>> _rows = new();
        private readonly List<KeyValuePair<string, Type>> _catalogRows = new();
        private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
        private string[] _terms = Array.Empty<string>();
        private string _filterText = string.Empty;
        private Type _selected;
        private KeyValuePair<string, Type>? _focusedEntry;
        private string _focusedControl;
        private KeyValuePair<string, Type>? _pressed;
        private Vector2 _pressPosition;
        private int _pressedPointerId;
        private bool _dragging;
        private int _generation;

        public VirtualizedTypeCatalogView(DataVisualizer owner, NamespaceController controller, Dictionary<string, List<Type>> managed, Dictionary<string, int> order)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _managed = managed ?? throw new ArgumentNullException(nameof(managed));
            _order = order ?? throw new ArgumentNullException(nameof(order));
            name = "virtualized-type-catalog";
            focusable = true;
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
            string normalized = text?.Trim() ?? string.Empty;
            if (string.Equals(_filterText, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            _filterText = normalized;
            _terms = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Refresh();
            return true;
        }

        public void Select(Type type, bool reveal = true)
        {
            if (_selected != type && _focusedEntry.HasValue && type != null)
            {
                _focusedEntry = new KeyValuePair<string, Type>(NamespaceController.GetNamespaceKey(type), type);
                _focusedControl = null;
            }
            _selected = type;
            if (reveal && type != null)
            {
                _collapsed.Remove(NamespaceController.GetNamespaceKey(type));
                Refresh();
                int index = Find(type);
                if (0 <= index)
                {
                    List.ScrollToItem(index);
                }
            }
            else
            {
                List.RefreshItems();
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
            int step = forward ? 1 : -1;
            for (int offset = 1; offset <= types.Count; offset++)
            {
                Type candidate = types[(index + step * offset + types.Count) % types.Count];
                if (0 <= Find(candidate))
                {
                    return candidate;
                }
            }
            return null;
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
                _catalogRows.Add(new KeyValuePair<string, Type>(group.Key, null));
                foreach (Type type in group.Value)
                {
                    _catalogRows.Add(new KeyValuePair<string, Type>(group.Key, type));
                }
            }
            _pressed = null;
            _dragging = false;
            Refresh();
            return true;
        }

        public void Refresh()
        {
            KeyValuePair<string, Type>? focused = _focusedEntry;
            string control = _focusedControl;
            _rows.Clear();
            VisibleNamespaceCount = 0;
            foreach (KeyValuePair<string, List<Type>> group in NamespaceController.GetOrderedNamespaceEntries(_managed, _order))
            {
                int header = _rows.Count;
                _rows.Add(new KeyValuePair<string, Type>(group.Key, null));
                bool matches = false;
                foreach (Type type in group.Value)
                {
                    bool match = true;
                    string title = NamespaceController.GetTypeDisplayName(type);
                    foreach (string term in _terms)
                    {
                        if (!title.Contains(term, StringComparison.OrdinalIgnoreCase))
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        matches = true;
                        if (!_collapsed.Contains(group.Key))
                        {
                            _rows.Add(new KeyValuePair<string, Type>(group.Key, type));
                        }
                    }
                }
                if (!matches && _terms.Length != 0)
                {
                    _rows.RemoveAt(header);
                }
                else
                {
                    VisibleNamespaceCount++;
                }
            }
            int generation = ++_generation;
            List.Rebuild();
            if (focused.HasValue)
            {
                KeyValuePair<string, Type> identity = focused.Value;
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

        public bool Drop(KeyValuePair<string, Type> source, KeyValuePair<string, Type> target, bool after)
        {
            if (
                _owner.IsAssetEditingSuspended
                || source.Equals(target)
                || source.Key == null
                || target.Key == null
                || !_managed.ContainsKey(source.Key)
                || !_managed.ContainsKey(target.Key)
            )
            {
                return false;
            }
            if (source.Value == null && target.Value == null)
            {
                List<string> keys = new(_managed.Count);
                foreach (KeyValuePair<string, List<Type>> group in NamespaceController.GetOrderedNamespaceEntries(_managed, _order))
                {
                    keys.Add(group.Key);
                }
                int from = keys.IndexOf(source.Key);
                int targetIndex = keys.IndexOf(target.Key);
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
                keys.Insert(destinationIndex, source.Key);
                for (int i = 0; i < keys.Count; i++)
                {
                    _order[keys[i]] = i;
                }
                _owner.PersistVirtualCatalogOrder(null, keys);
            }
            else if (
                source.Value != null
                && target.Value != null
                && string.Equals(source.Key, target.Key, StringComparison.Ordinal)
                && _managed.TryGetValue(source.Key, out List<Type> types)
            )
            {
                int from = types.IndexOf(source.Value);
                int to = types.IndexOf(target.Value);
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
                types.Insert(destinationIndex, source.Value);
                _owner.PersistVirtualCatalogOrder(source.Key, null);
            }
            else
            {
                return false;
            }
            Reconcile();
            return true;
        }

        private bool CheckEntry(string key, Type type, int index)
        {
            return _catalogRows.Count <= index || _catalogRows[index].Value != type || !string.Equals(_catalogRows[index].Key, key, StringComparison.Ordinal);
        }

        private bool IsSelected(KeyValuePair<string, Type> entry)
        {
            return _selected != null
                && (entry.Value == null ? string.Equals(entry.Key, NamespaceController.GetNamespaceKey(_selected), StringComparison.Ordinal) : entry.Value == _selected);
        }

        private int NamespacePosition(string key)
        {
            int position = 0;
            foreach (KeyValuePair<string, Type> entry in _catalogRows)
            {
                if (entry.Value == null)
                {
                    if (string.Equals(entry.Key, key, StringComparison.Ordinal))
                    {
                        return position;
                    }
                    position++;
                }
            }
            return -1;
        }

        private int Find(Type type)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Value == type && type != null)
                {
                    return i;
                }
            }
            return -1;
        }

        private VisualElement FindRealized(KeyValuePair<string, Type> identity)
        {
            return List.Query<VisualElement>(className: StyleConstants.VirtualCatalogRowClass)
                .ToList()
                .Find(row => row.userData is KeyValuePair<string, Type> entry && entry.Equals(identity));
        }

        private VisualElement MakeRow()
        {
            VisualElement row = new() { focusable = true };
            row.AddToClassList(StyleConstants.VirtualCatalogRowClass);
            Button collapse = new(() => Toggle((KeyValuePair<string, Type>)row.userData)) { name = "catalog-collapse" };
            collapse.AddToClassList(StyleConstants.NamespaceIndicatorClass);
            row.Add(collapse);
            Button up = new(() => Move((KeyValuePair<string, Type>)row.userData, true)) { name = "go-up-button", text = "↑" };
            Button down = new(() => Move((KeyValuePair<string, Type>)row.userData, false)) { name = "go-down-button", text = "↓" };
            row.Add(up);
            row.Add(down);
            Label label = new() { name = "catalog-label" };
            label.AddToClassList(StyleConstants.TypeLabelClass);
            row.Add(label);
            Button remove = new(() => Remove((KeyValuePair<string, Type>)row.userData, row)) { name = "catalog-remove", text = "X" };
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
                _focusedEntry = (KeyValuePair<string, Type>)row.userData;
                _focusedControl = (evt.target as VisualElement)?.name;
            });
            row.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (_owner.IsAssetEditingSuspended)
                {
                    return;
                }
                KeyValuePair<string, Type> entry = (KeyValuePair<string, Type>)row.userData;
                if (evt.keyCode == KeyCode.Escape && _pressed.HasValue)
                {
                    row.ReleasePointer(_pressedPointerId);
                }
                else if (entry.Value != null && (evt.keyCode == KeyCode.UpArrow || evt.keyCode == KeyCode.DownArrow))
                {
                    Type next = Adjacent(entry.Value, evt.keyCode == KeyCode.DownArrow);
                    if (next != null)
                    {
                        _controller.SelectType(_owner, next);
                    }
                }
                else if (entry.Value == null && (evt.keyCode == KeyCode.LeftArrow || evt.keyCode == KeyCode.RightArrow))
                {
                    SetCollapsed(entry.Key, evt.keyCode == KeyCode.LeftArrow);
                }
                else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter || evt.keyCode == KeyCode.Space)
                {
                    if (evt.target != row)
                    {
                        return;
                    }
                    Activate(entry);
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
                if (evt.button == 0 && !_owner.IsAssetEditingSuspended)
                {
                    row.Focus();
                    _pressed = (KeyValuePair<string, Type>)row.userData;
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
                    _dragging = _dragging || 6 < Vector2.Distance(_pressPosition, evt.position);
                    row.EnableInClassList(StyleConstants.SelectedClass, _dragging || IsSelected((KeyValuePair<string, Type>)row.userData));
                }
            });
            row.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!_pressed.HasValue || !row.HasPointerCapture(evt.pointerId))
                {
                    return;
                }
                KeyValuePair<string, Type> source = _pressed.Value;
                bool dragging = _dragging;
                _pressed = null;
                _dragging = false;
                row.ReleasePointer(evt.pointerId);
                if (!_owner.IsAssetEditingSuspended)
                {
                    if (dragging)
                    {
                        VisualElement target = panel.Pick(evt.position);
                        while (target != null && target.userData is not KeyValuePair<string, Type>)
                        {
                            target = target.parent;
                        }
                        if (target?.userData is KeyValuePair<string, Type> destination)
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
                row.EnableInClassList(StyleConstants.SelectedClass, IsSelected((KeyValuePair<string, Type>)row.userData));
            });
            VisualElement slot = new();
            slot.style.height = 40;
            slot.Add(row);
            return slot;
        }

        private void BindRow(VisualElement slot, int index)
        {
            VisualElement row = slot[0];
            KeyValuePair<string, Type> entry = _rows[index];
            if (!_managed.TryGetValue(entry.Key, out List<Type> types))
            {
                row.style.display = DisplayStyle.None;
                return;
            }
            row.style.display = DisplayStyle.Flex;
            row.userData = entry;
            bool header = entry.Value == null;
            string title = header ? entry.Key : NamespaceController.GetTypeDisplayName(entry.Value);
            row.name = header ? "namespace-header-" + entry.Key : "type-item-" + entry.Value.Name;
            row.EnableInClassList(StyleConstants.TypeItemClass, !header);
            row.EnableInClassList(StyleConstants.NamespaceHeaderClass, header);
            row.EnableInClassList(StyleConstants.SelectedClass, IsSelected(entry));
            Label label = row.Q<Label>("catalog-label");
            label.text = title;
            label.tooltip = header ? entry.Key : entry.Value.FullName;
            label.EnableInClassList(StyleConstants.BoldClass, header);
            Button collapse = row.Q<Button>("catalog-collapse");
            collapse.style.display = header ? DisplayStyle.Flex : DisplayStyle.None;
            collapse.text = _collapsed.Contains(entry.Key) ? StyleConstants.ArrowCollapsed : StyleConstants.ArrowExpanded;
            collapse.tooltip = "Collapse or expand " + title + " (Enter/Space; Left/Right)";
            Button up = row.Q<Button>("go-up-button");
            Button down = row.Q<Button>("go-down-button");
            up.tooltip = "Move " + title + " to top";
            down.tooltip = "Move " + title + " to bottom";
            int position = header ? NamespacePosition(entry.Key) : types.IndexOf(entry.Value);
            int count = header ? _managed.Count : types.Count;
            up.SetEnabled(!_owner.IsAssetEditingSuspended && position != 0);
            down.SetEnabled(!_owner.IsAssetEditingSuspended && position != count - 1);
            Button remove = row.Q<Button>("catalog-remove");
            int removable = 0;
            if (header)
            {
                foreach (Type type in types)
                {
                    if (NamespaceController.IsTypeRemovable(type))
                    {
                        removable++;
                    }
                }
            }
            remove.style.display = header
                ? (1 < removable ? DisplayStyle.Flex : DisplayStyle.None)
                : (NamespaceController.IsTypeRemovable(entry.Value) ? DisplayStyle.Flex : DisplayStyle.None);
            remove.tooltip = "Remove " + title + " from DxVisualizer";
        }

        private void Activate(KeyValuePair<string, Type> entry)
        {
            if (entry.Value == null)
            {
                Toggle(entry);
            }
            else
            {
                _controller.SelectType(_owner, entry.Value);
            }
        }

        private void Toggle(KeyValuePair<string, Type> entry)
        {
            if (!_owner.IsAssetEditingSuspended)
            {
                SetCollapsed(entry.Key, !_collapsed.Contains(entry.Key));
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

        private void Remove(KeyValuePair<string, Type> entry, VisualElement anchor)
        {
            if (_owner.IsAssetEditingSuspended)
            {
                return;
            }
            if (entry.Value == null)
            {
                List<Type> removable = new();
                foreach (Type type in _managed[entry.Key])
                {
                    if (NamespaceController.IsTypeRemovable(type))
                    {
                        removable.Add(type);
                    }
                }
                _owner.BuildAndOpenConfirmationPopover(
                    "Remove " + removable.Count + " non-core types from '" + entry.Key + "'?",
                    "Remove",
                    () => _controller.RemoveCatalogNamespaceTypes(_owner, entry.Key, removable),
                    anchor
                );
                return;
            }
            _owner.BuildAndOpenConfirmationPopover(
                "Remove type '" + entry.Value.Name + "' from DxVisualizer?",
                "Remove",
                () => _controller.RemoveCatalogType(_owner, entry.Value),
                anchor
            );
        }

        private void Move(KeyValuePair<string, Type> entry, bool top)
        {
            if (_owner.IsAssetEditingSuspended || !_managed.TryGetValue(entry.Key, out List<Type> types))
            {
                return;
            }
            KeyValuePair<string, Type> destination =
                entry.Value == null
                    ? new KeyValuePair<string, Type>(NamespaceController.GetOrderedNamespaceEntries(_managed, _order)[top ? 0 : _managed.Count - 1].Key, null)
                    : new KeyValuePair<string, Type>(entry.Key, types[top ? 0 : types.Count - 1]);
            Drop(entry, destination, !top);
        }
    }
}
