namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using System;
    using System.Collections.Generic;
    using Extensions;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>Serializable package-owned input. Each owner has independent defaults when no profile is assigned.</summary>
    [Serializable]
    public sealed class DataVisualizerInputSettings
    {
        public CatalogInputSettings Navigation => navigation ??= new CatalogInputSettings();

        /*
            macOS keyboards send Backspace for the key labeled Delete, so the alternate binding
            can also delete there. Captions name every key that runs a command on this platform.
        */
        public bool IsAlternateDeleteAvailable => !alternateDeleteOnMacOnly || Application.platform == RuntimePlatform.OSXEditor;

        public EditorInputBinding ActiveAlternateDelete => IsAlternateDeleteAvailable ? alternateDelete : default;

        public EditorInputBinding nextFocus = new(KeyCode.Tab);
        public EditorInputBinding previousFocus = new(KeyCode.Tab, EventModifiers.Shift);
        public EditorInputBinding clone = new(KeyCode.D, actionModifier: true);
        public EditorInputBinding rename = new(KeyCode.F2);
        public EditorInputBinding delete = new(KeyCode.Delete);
        public EditorInputBinding alternateDelete = new(KeyCode.Backspace);
        public bool alternateDeleteOnMacOnly = true;
        public EditorInputBinding moveFirst = new(KeyCode.Home, actionModifier: true);
        public EditorInputBinding moveLast = new(KeyCode.End, actionModifier: true);
        public EditorInputBinding actions = new(KeyCode.F10, EventModifiers.Shift);
        public EditorInputBinding alternateActions = new(KeyCode.Menu);
        public EventModifiers rangeSelectionModifiers = EventModifiers.Shift;
        public EditorInputBinding toggleSelection = new(KeyCode.Space, actionModifier: true);

        [SerializeField]
        private CatalogInputSettings navigation = new();

        private static string ShortcutOrNull(string action, EditorInputBinding binding, bool menuLabel)
        {
            if (binding.key == KeyCode.None)
            {
                return null;
            }
            string shortcut = menuLabel ? binding.ToMenuString() : binding.ToString();
            return string.Equals(shortcut, action, StringComparison.OrdinalIgnoreCase) ? null : shortcut;
        }

        /*
            Builds one caption with at most one intermediate join, so the common paths allocate
            one string and the two-key path allocates two.
        */
        private static string Caption(string action, string shortcut, string alternateShortcut)
        {
            if (shortcut == null && alternateShortcut == null)
            {
                return action;
            }
            if (shortcut == null)
            {
                shortcut = alternateShortcut;
            }
            else if (alternateShortcut != null)
            {
                shortcut = shortcut + ", " + alternateShortcut;
            }
            return $"{action} ({shortcut})";
        }

        /*
            Names one search command. A cleared key cannot run the command, and a printable key
            types into a focused search field instead, so both drop out. A command keeps its
            alternate key when the primary one is unusable.
        */
        private static string BuildCommandHint(HashSet<KeyCode> claimed, KeyCode primary, KeyCode alternate, string action, bool allowPrintable)
        {
            bool hasPrimary = IsHintKey(primary, claimed, allowPrintable);
            bool hasAlternate = alternate != primary && IsHintKey(alternate, claimed, allowPrintable);
            if (!hasPrimary && !hasAlternate)
            {
                return null;
            }
            if (hasPrimary)
            {
                claimed.Add(primary);
            }
            if (hasAlternate)
            {
                claimed.Add(alternate);
            }
            return hasPrimary && hasAlternate ? $"{primary}/{alternate} {action}" : $"{(hasPrimary ? primary : alternate)} {action}";
        }

        private static bool IsHintKey(KeyCode key, HashSet<KeyCode> claimed, bool allowPrintable)
        {
            return key != KeyCode.None && !claimed.Contains(key) && (allowPrintable || !CatalogInputSettings.IsPrintableKey(key));
        }

        /*
            Focus routing runs before command hints. It matches a focus binding's exact
            modifiers, and the router always consumes a bare Tab even when both focus
            bindings are remapped, so only an unmodified focus key and Tab are claimed.
        */
        private static void AddFocusClaims(HashSet<KeyCode> claimed, EditorInputBinding next, EditorInputBinding previous)
        {
            claimed.Add(KeyCode.Tab);
            if (next.key != KeyCode.None && next.MatchesModifiers(EventModifiers.None))
            {
                claimed.Add(next.key);
            }
            if (previous.key != KeyCode.None && previous.MatchesModifiers(EventModifiers.None))
            {
                claimed.Add(previous.key);
            }
        }

        public bool HandleButton(KeyDownEvent evt, CatalogInputSettings navigation = null)
        {
            VisualElement target = evt.target as VisualElement;
            Button button = target as Button ?? target?.GetFirstAncestorOfType<Button>();
            if (button == null || !button.enabledInHierarchy)
            {
                return false;
            }
            if ((navigation ?? Navigation).IsSelectionKey(evt.keyCode))
            {
                using NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled();
                submit.target = button;
                button.SendEvent(submit);
            }
            else if (!CatalogInputSettings.IsUnitySubmitKey(evt.keyCode))
            {
                return false;
            }
            evt.PreventDefaultCompat();
            evt.StopImmediatePropagation();
            return true;
        }

        public bool IsDelete(IKeyboardEvent evt)
        {
            return delete.Matches(evt) || (IsAlternateDeleteAvailable && alternateDelete.Matches(evt));
        }

        /*
            Names a command with every key that runs it for a Unity menu label. A binding whose
            key repeats the command name adds nothing ("Delete (Delete)"), and a cleared key
            cannot run the command at all. Two keys join with a comma because GenericMenu reads
            a slash as a submenu separator.
        */
        public string ActionCaption(string action, EditorInputBinding binding, EditorInputBinding alternate = default)
        {
            return Caption(action, ShortcutOrNull(action, binding, true), ShortcutOrNull(action, alternate, true));
        }

        public string AssetActionHint()
        {
            return $"{ProseCaption("Clone", clone)}, {ProseCaption("Rename", rename)}, Move, {DescribeOrderHint()}, {ProseCaption("Delete", delete, ActiveAlternateDelete)}. Actions: {actions}/{alternateActions}.";
        }

        /*
            Names the keys that drive the quick search results and the browse-all list. Each
            command keeps whichever of its keys can run in the active context: a cleared key
            drops out, a key another command already runs drops out, and, while the search
            field holds focus, a printable key drops out because the field keeps it for text
            entry. Close is routed before select and open, so it claims its key first even
            though the hint reads select, open, close.
        */
        public string SearchResultHint(bool searchFieldHasFocus = true)
        {
            CatalogInputSettings navigation = Navigation;
            bool allowPrintable = !searchFieldHasFocus;
            HashSet<KeyCode> claimed = new();
            AddFocusClaims(claimed, nextFocus, previousFocus);
            string close = BuildCommandHint(claimed, navigation.CancelDragKey, KeyCode.None, "close", allowPrintable);
            string select = BuildCommandHint(claimed, navigation.PreviousTypeKey, navigation.NextTypeKey, "select", allowPrintable);
            string open = BuildCommandHint(claimed, navigation.SelectKey, navigation.AlternateSelectKey, "open", allowPrintable);
            List<string> parts = new(3);
            if (select != null)
            {
                parts.Add(select);
            }
            if (open != null)
            {
                parts.Add(open);
            }
            if (close != null)
            {
                parts.Add(close);
            }
            return string.Join(" · ", parts);
        }

        /*
            Names the keys that move through the inspector label suggestions, add the chosen
            suggestion and close the list. The field keeps printable keys for typing, so they
            never appear here even when a command uses one, and the commands are claimed in
            routing order so a shared key names only the command that runs it.
        */
        public string LabelSuggestionHint()
        {
            CatalogInputSettings navigation = Navigation;
            HashSet<KeyCode> claimed = new();
            AddFocusClaims(claimed, nextFocus, previousFocus);
            string choose = BuildCommandHint(claimed, navigation.PreviousTypeKey, navigation.NextTypeKey, "choose", allowPrintable: false);
            string add = BuildCommandHint(claimed, navigation.SelectKey, navigation.AlternateSelectKey, "add", allowPrintable: false);
            string close = BuildCommandHint(claimed, navigation.CancelDragKey, KeyCode.None, "close", allowPrintable: false);
            List<string> parts = new(3);
            if (choose != null)
            {
                parts.Add(choose);
            }
            if (add != null)
            {
                parts.Add(add);
            }
            if (close != null)
            {
                parts.Add(close);
            }
            return string.Join(" · ", parts);
        }

        /*
            The Actions hint is one line of prose, so it keeps the cross-platform Ctrl/Cmd
            spelling that the rest of the help text uses.
        */
        private string ProseCaption(string action, EditorInputBinding binding, EditorInputBinding alternate = default)
        {
            return Caption(action, ShortcutOrNull(action, binding, false), ShortcutOrNull(action, alternate, false));
        }

        /*
            Names the ordering shortcuts that exist. One cleared ordering key does not hide the
            other, so the hint still tells the user which move they can make.
        */
        private string DescribeOrderHint()
        {
            bool hasFirst = moveFirst.key != KeyCode.None;
            bool hasLast = moveLast.key != KeyCode.None;
            if (!hasFirst && !hasLast)
            {
                return "Order";
            }
            if (!hasFirst)
            {
                return $"Order ({moveLast})";
            }
            if (!hasLast)
            {
                return $"Order ({moveFirst})";
            }
            return $"Order ({moveFirst}/{moveLast})";
        }
    }
}
