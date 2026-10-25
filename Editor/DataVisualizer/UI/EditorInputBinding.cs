namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using System;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>A keyboard action with exact modifiers and optional cross-platform Control/Command support.</summary>
    [Serializable]
    public struct EditorInputBinding
    {
        public KeyCode key;
        public EventModifiers modifiers;
        public bool actionModifier;

        public EditorInputBinding(KeyCode key, EventModifiers modifiers = EventModifiers.None, bool actionModifier = false)
        {
            this.key = key;
            this.modifiers = modifiers;
            this.actionModifier = actionModifier;
        }

        public bool Matches(IKeyboardEvent evt)
        {
            return key != KeyCode.None && evt.keyCode == key && MatchesModifiers(evt.modifiers);
        }

        public bool MatchesModifiers(EventModifiers actual)
        {
            const EventModifiers relevant = EventModifiers.Control | EventModifiers.Command | EventModifiers.Shift | EventModifiers.Alt;
            actual &= relevant;
            EventModifiers expected = modifiers & relevant;
            if (actionModifier)
            {
                if ((actual & (EventModifiers.Control | EventModifiers.Command)) == 0)
                {
                    return false;
                }
                actual &= ~(EventModifiers.Control | EventModifiers.Command);
                expected &= ~(EventModifiers.Control | EventModifiers.Command);
            }
            return actual == expected;
        }

        public override string ToString()
        {
            if (key == KeyCode.None)
            {
                return "Unbound";
            }
            EventModifiers display = modifiers & (EventModifiers.Control | EventModifiers.Command | EventModifiers.Shift | EventModifiers.Alt);
            if (actionModifier)
            {
                display &= ~(EventModifiers.Control | EventModifiers.Command);
            }
            return (actionModifier ? "Ctrl/Cmd+" : string.Empty) + (display == EventModifiers.None ? string.Empty : display + "+") + key;
        }
    }
}
