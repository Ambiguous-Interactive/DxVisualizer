namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using System;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>Catalog row bindings. Existing selection/navigation keys take precedence over boundary keys. Unity buttons retain their platform activation behavior.</summary>
    [Serializable]
    public sealed class CatalogInputSettings
    {
        // Six logical points ignore small pointer jitter before a drag starts.
        public const float DefaultDragThresholdPoints = 6f;
        public float DragThresholdPoints
        {
            get => float.IsNaN(dragThresholdPoints) || float.IsInfinity(dragThresholdPoints) ? DefaultDragThresholdPoints : Math.Max(0, dragThresholdPoints);
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
        [Min(0)]
        private float dragThresholdPoints = DefaultDragThresholdPoints;

        /// <summary>Printable input belongs to the focused text field even when a navigation binding uses that letter.</summary>
        public static bool IsTextEntry(EventBase evt)
        {
            return evt is IKeyboardEvent key
                && key.character != '\0'
                && !char.IsControl(key.character)
                && evt.target is VisualElement target
                && (target is TextField || target.GetFirstAncestorOfType<TextField>() != null);
        }

        public static bool IsUnitySubmitKey(KeyCode key) => key is KeyCode.Return or KeyCode.KeypadEnter or KeyCode.Space;

        public static bool MatchesKey(KeyCode actual, KeyCode configured) => configured != KeyCode.None && actual == configured;

        /*
            A focused text field keeps printable input for text entry (see IsTextEntry), so a
            navigation or command binding on a letter, digit or punctuation key never runs
            from the search box. Hints must not advertise those keys. Unity numbers every
            printable key contiguously from Space to Tilde; control keys sit outside that
            range. The numeric keypad sits elsewhere and also sends a character, so its
            digits and operators count as printable while its Enter key stays a submit key.
        */
        public static bool IsPrintableKey(KeyCode key)
        {
            if ((int)KeyCode.Space <= (int)key && (int)key <= (int)KeyCode.Tilde)
            {
                return true;
            }
            return key
                is KeyCode.Keypad0
                    or KeyCode.Keypad1
                    or KeyCode.Keypad2
                    or KeyCode.Keypad3
                    or KeyCode.Keypad4
                    or KeyCode.Keypad5
                    or KeyCode.Keypad6
                    or KeyCode.Keypad7
                    or KeyCode.Keypad8
                    or KeyCode.Keypad9
                    or KeyCode.KeypadPeriod
                    or KeyCode.KeypadDivide
                    or KeyCode.KeypadMultiply
                    or KeyCode.KeypadMinus
                    or KeyCode.KeypadPlus
                    or KeyCode.KeypadEquals;
        }

        public static bool IsNativeNavigationKey(KeyCode key)
        {
            return key
                is KeyCode.UpArrow
                    or KeyCode.DownArrow
                    or KeyCode.Home
                    or KeyCode.End
                    or KeyCode.PageUp
                    or KeyCode.PageDown
                    or KeyCode.Space
                    or KeyCode.Return
                    or KeyCode.KeypadEnter;
        }

        public KeyCode ToNativeKey(KeyCode key)
        {
            if (MatchesKey(key, PreviousTypeKey))
            {
                return KeyCode.UpArrow;
            }
            if (MatchesKey(key, NextTypeKey))
            {
                return KeyCode.DownArrow;
            }
            if (IsConfirmKey(key))
            {
                return KeyCode.Return;
            }
            if (MatchesKey(key, ToggleKey))
            {
                return KeyCode.Space;
            }
            if (MatchesKey(key, FirstRowKey))
            {
                return KeyCode.Home;
            }
            if (MatchesKey(key, LastRowKey))
            {
                return KeyCode.End;
            }
            if (MatchesKey(key, PreviousPageKey))
            {
                return KeyCode.PageUp;
            }
            if (MatchesKey(key, NextPageKey))
            {
                return KeyCode.PageDown;
            }
            return KeyCode.None;
        }

        public bool IsConfirmKey(KeyCode key) => MatchesKey(key, SelectKey) || MatchesKey(key, AlternateSelectKey);

        public bool IsConfirmCharacter(KeyCode key, char character)
        {
            return key == KeyCode.None && character is '\n' or '\r' && (IsConfirmKey(KeyCode.Return) || IsConfirmKey(KeyCode.KeypadEnter));
        }

        public bool IsSelectionKey(KeyCode key) => IsConfirmKey(key) || MatchesKey(key, ToggleKey);

        public bool IsSelectionKey(IKeyboardEvent evt) => IsSelectionKey(evt.keyCode) && new EditorInputBinding(evt.keyCode).MatchesModifiers(evt.modifiers);

        public bool HasPassedDragThreshold(Vector2 start, Vector2 current) => DragThresholdPoints * DragThresholdPoints < (current - start).sqrMagnitude;
    }
}
