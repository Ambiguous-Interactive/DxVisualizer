namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using System;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>Serializable package-owned input. Each owner has independent defaults when no profile is assigned.</summary>
    [Serializable]
    public sealed class DataVisualizerInputSettings
    {
        public CatalogInputSettings Navigation => navigation ??= new CatalogInputSettings();

        public EditorInputBinding clone = new(KeyCode.D, actionModifier: true);
        public EditorInputBinding rename = new(KeyCode.F2);
        public EditorInputBinding delete = new(KeyCode.Delete);
        public EditorInputBinding alternateDelete = new(KeyCode.Backspace);
        public bool alternateDeleteOnMacOnly = true;
        public EditorInputBinding moveFirst = new(KeyCode.Home, actionModifier: true);
        public EditorInputBinding moveLast = new(KeyCode.End, actionModifier: true);
        public EditorInputBinding actions = new(KeyCode.F10, EventModifiers.Shift);
        public EditorInputBinding alternateActions = new(KeyCode.Menu);
        public EditorInputBinding toggleSelection = new(KeyCode.Space, actionModifier: true);

        [SerializeField]
        private CatalogInputSettings navigation = new();

        public bool IsDelete(IKeyboardEvent evt)
        {
            return delete.Matches(evt) || ((!alternateDeleteOnMacOnly || Application.platform == RuntimePlatform.OSXEditor) && alternateDelete.Matches(evt));
        }

        public string AssetActionHint()
        {
            return $"Clone ({clone}), Rename ({rename}), Move, Order ({moveFirst}/{moveLast}), Delete ({delete}). Actions: {actions}/{alternateActions}.";
        }
    }
}
