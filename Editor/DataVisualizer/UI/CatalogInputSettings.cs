namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using System;
    using UnityEngine;

    /// <summary>Catalog row bindings. Existing selection/navigation keys take precedence over boundary keys. Unity buttons retain their platform activation behavior.</summary>
    [Serializable]
    public sealed class CatalogInputSettings
    {
        // Six logical points ignore small pointer jitter before a drag starts.
        public const float DefaultDragThresholdPoints = 6f;
        public float DragThresholdPoints
        {
            get => dragThresholdPoints;
            set
            {
                if (value < 0 || float.IsNaN(value) || float.IsInfinity(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                dragThresholdPoints = value;
            }
        }

        public KeyCode SelectKey = KeyCode.Return;
        public KeyCode AlternateSelectKey = KeyCode.KeypadEnter;
        public KeyCode ToggleKey = KeyCode.Space;
        public KeyCode PreviousTypeKey = KeyCode.UpArrow;
        public KeyCode NextTypeKey = KeyCode.DownArrow;
        public KeyCode FirstRowKey = KeyCode.Home;
        public KeyCode LastRowKey = KeyCode.End;
        public KeyCode PreviousPageKey = KeyCode.PageUp;
        public KeyCode NextPageKey = KeyCode.PageDown;
        public KeyCode CollapseKey = KeyCode.LeftArrow;
        public KeyCode ExpandKey = KeyCode.RightArrow;
        public KeyCode CancelDragKey = KeyCode.Escape;
        public int PointerButton;

        [SerializeField]
        private float dragThresholdPoints = DefaultDragThresholdPoints;

        public static bool MatchesKey(KeyCode actual, KeyCode configured) => configured != KeyCode.None && actual == configured;

        public bool IsConfirmKey(KeyCode key) => MatchesKey(key, SelectKey) || MatchesKey(key, AlternateSelectKey);

        public bool IsConfirmCharacter(KeyCode key, char character)
        {
            return key == KeyCode.None && character is '\n' or '\r' && (IsConfirmKey(KeyCode.Return) || IsConfirmKey(KeyCode.KeypadEnter));
        }

        public bool IsSelectionKey(KeyCode key) => IsConfirmKey(key) || MatchesKey(key, ToggleKey);

        public bool HasPassedDragThreshold(Vector2 start, Vector2 current) => dragThresholdPoints * dragThresholdPoints < (current - start).sqrMagnitude;
    }
}
