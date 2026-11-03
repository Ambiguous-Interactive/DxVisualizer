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
            return delete.Matches(evt) || ((!alternateDeleteOnMacOnly || Application.platform == RuntimePlatform.OSXEditor) && alternateDelete.Matches(evt));
        }

        /*
            Names a command with the key that runs it, so the row menu and the Actions hint show
            the same shortcut. A binding whose key repeats the command name adds nothing
            ("Delete (Delete)"), and a cleared key cannot run the command at all.
        */
        public string ActionCaption(string action, EditorInputBinding binding)
        {
            string shortcut = binding.ToString();
            if (binding.key == KeyCode.None || string.Equals(shortcut, action, StringComparison.OrdinalIgnoreCase))
            {
                return action;
            }
            return action + " (" + shortcut + ")";
        }

        public string AssetActionHint()
        {
            string order = moveFirst.key == KeyCode.None || moveLast.key == KeyCode.None ? "Order" : $"Order ({moveFirst}/{moveLast})";
            return $"{ActionCaption("Clone", clone)}, {ActionCaption("Rename", rename)}, Move, {order}, {ActionCaption("Delete", delete)}. Actions: {actions}/{alternateActions}.";
        }
    }
}
