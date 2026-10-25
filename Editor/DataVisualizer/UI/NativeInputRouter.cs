namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using Extensions;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Utilities;

    /// <summary>Translates configured gestures at Unity's native-control boundary. Literal native keys/buttons belong only here and in input defaults.</summary>
    public sealed class NativeInputRouter
    {
        public bool IsRouting { get; private set; }

        private readonly ReusableDisposalScope<NativeInputRouter> _scope = new(static router => router.IsRouting = false);

        private static void Consume(EventBase evt)
        {
            evt.imguiEvent?.Use();
            evt.PreventDefaultCompat();
            evt.StopImmediatePropagation();
        }

        public bool HandleFocusOrButton(KeyDownEvent evt, DataVisualizerInputSettings input)
        {
            if (IsRouting)
            {
                return false;
            }
            if (evt.target is VisualElement target && (target is ListView || target.GetFirstAncestorOfType<ListView>() != null))
            {
                CatalogInputSettings bindings = target.GetFirstAncestorOfType<VirtualizedTypeCatalogView>()?.Input ?? input.Navigation;
                bool navigation = bindings.ToNativeKey(evt.keyCode) != KeyCode.None;
                bool button = target is Button || target.GetFirstAncestorOfType<Button>() != null;
                bool catalog = target.GetFirstAncestorOfType<VirtualizedTypeCatalogView>() != null;
                bool rowNavigation =
                    CatalogInputSettings.MatchesKey(evt.keyCode, bindings.PreviousTypeKey)
                    || CatalogInputSettings.MatchesKey(evt.keyCode, bindings.NextTypeKey)
                    || CatalogInputSettings.MatchesKey(evt.keyCode, bindings.CollapseKey)
                    || CatalogInputSettings.MatchesKey(evt.keyCode, bindings.ExpandKey);
                if (catalog && rowNavigation)
                {
                    return false;
                }
                if (navigation)
                {
                    return button && input.HandleButton(evt, bindings);
                }
            }
            if (input.nextFocus.Matches(evt))
            {
                return RouteKey(evt, KeyCode.Tab, EventModifiers.None);
            }
            if (input.previousFocus.Matches(evt))
            {
                return RouteKey(evt, KeyCode.Tab, EventModifiers.Shift);
            }
            if (evt.keyCode == KeyCode.Tab)
            {
                Consume(evt);
                return true;
            }
            return input.HandleButton(evt);
        }

        public bool HandleListKey(EventBase evt, DataVisualizerInputSettings input)
        {
            if (IsRouting || evt.target is VisualElement target && (target is Button || target.GetFirstAncestorOfType<Button>() != null))
            {
                return false;
            }
            IKeyboardEvent key = (IKeyboardEvent)evt;
            if (input.toggleSelection.Matches(key))
            {
                EventModifiers action = Application.platform == RuntimePlatform.OSXEditor ? EventModifiers.Command : EventModifiers.Control;
                return RouteKey(evt, KeyCode.Space, action);
            }
            KeyCode native = input.Navigation.ToNativeKey(key.keyCode);
            if (native != KeyCode.None)
            {
                return RouteKey(evt, native, key.modifiers);
            }
            if (CatalogInputSettings.IsNativeNavigationKey(key.keyCode))
            {
                Consume(evt);
                return true;
            }
            return false;
        }

        private bool RouteKey(EventBase evt, KeyCode key, EventModifiers modifiers)
        {
            IKeyboardEvent original = (IKeyboardEvent)evt;
            if (original.keyCode == key && original.modifiers == modifiers)
            {
                return false;
            }
            if (evt.target is not VisualElement target)
            {
                return false;
            }
            using ReusableDisposalLease<NativeInputRouter> scope = _scope.Acquire(this);
            IsRouting = true;
            if (evt is KeyUpEvent)
            {
                using KeyUpEvent forwarded = KeyUpEvent.GetPooled(original.character, key, modifiers);
                forwarded.target = target;
                target.SendEvent(forwarded);
            }
            else
            {
                using KeyDownEvent forwarded = KeyDownEvent.GetPooled(original.character, key, modifiers);
                forwarded.target = target;
                target.SendEvent(forwarded);
            }
            Consume(evt);
            return true;
        }
    }
}
