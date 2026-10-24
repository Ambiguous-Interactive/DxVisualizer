namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using System;
    using UnityEngine;

    /// <summary>Catalog row bindings. Unity buttons retain their platform activation behavior.</summary>
    public sealed class CatalogInputSettings
    {
        // Six logical points ignore small pointer jitter before a drag starts.
        public const float DefaultDragThresholdPoints = 6f;

        public KeyCode SelectKey { get; set; } = KeyCode.Return;
        public KeyCode AlternateSelectKey { get; set; } = KeyCode.KeypadEnter;
        public KeyCode ToggleKey { get; set; } = KeyCode.Space;
        public KeyCode PreviousTypeKey { get; set; } = KeyCode.UpArrow;
        public KeyCode NextTypeKey { get; set; } = KeyCode.DownArrow;
        public KeyCode CollapseKey { get; set; } = KeyCode.LeftArrow;
        public KeyCode ExpandKey { get; set; } = KeyCode.RightArrow;
        public KeyCode CancelDragKey { get; set; } = KeyCode.Escape;
        public int PointerButton { get; set; }
        public float DragThresholdPoints
        {
            get => _dragThresholdPoints;
            set
            {
                if (value < 0 || float.IsNaN(value) || float.IsInfinity(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                _dragThresholdPoints = value;
            }
        }

        private float _dragThresholdPoints = DefaultDragThresholdPoints;

        public bool IsSelectionKey(KeyCode key) => key == SelectKey || key == AlternateSelectKey || key == ToggleKey;

        public bool HasPassedDragThreshold(Vector2 start, Vector2 current) => _dragThresholdPoints * _dragThresholdPoints < (current - start).sqrMagnitude;
    }
}
