namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using System;
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
