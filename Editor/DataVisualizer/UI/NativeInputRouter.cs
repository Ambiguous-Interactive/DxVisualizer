namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using System.Collections.Generic;
    using Extensions;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Utilities;

    /// <summary>Translates configured gestures at Unity's native-control boundary. Literal native keys/buttons belong only here and in input defaults.</summary>
    public sealed class NativeInputRouter
    {
        public bool IsRouting { get; private set; }
        public int PendingCount => _forwarded.Count;

        private readonly Dictionary<EventBase, (VisualElement Owner, object Identity, IPanel Panel)> _forwarded = new();
        private readonly ReusableDisposalScope<NativeInputRouter> _scope = new(static router => router.IsRouting = false);

        private static (VisualElement Owner, object Identity, IPanel Panel) CaptureIdentity(VisualElement target, Vector2 position)
        {
            VisualElement picked = target.panel?.Pick(position) ?? target;
            for (VisualElement element = picked; element != null; element = element.parent)
            {
                if (element.userData != null)
                {
                    return (element, element.userData, target.panel);
                }
            }
            return (target, null, target.panel);
        }

        private static char NativeCharacter(KeyCode key)
        {
            return key switch
            {
                KeyCode.Tab => '\t',
                KeyCode.Return or KeyCode.KeypadEnter => '\n',
                KeyCode.Space => ' ',
                _ => '\0',
            };
        }

        private static void Consume(EventBase evt)
        {
            evt.imguiEvent?.Use();
            evt.PreventDefaultCompat();
            evt.StopImmediatePropagation();
        }

        private static bool MoveFocus(KeyDownEvent evt, bool previous)
        {
            if (evt.target is not VisualElement target || target.panel == null)
            {
                return false;
            }
            IPanel panel = target.panel;
            VisualElementFocusRing ring = new(panel.visualTree);
            Focusable next = ring.GetNextFocusable(
                panel.focusController.focusedElement,
                previous ? VisualElementFocusChangeDirection.left : VisualElementFocusChangeDirection.right
            );
            next?.Focus();
            Consume(evt);
            return true;
        }

        public bool AcceptForwarded(EventBase evt)
        {
            if (!_forwarded.TryGetValue(evt, out var captured))
            {
                return false;
            }
            _forwarded.Remove(evt);
            bool identityMatches = captured.Identity is Object asset
                ? asset != null && ReferenceEquals(asset, captured.Owner.userData)
                : Equals(captured.Identity, captured.Owner.userData);
            if (captured.Owner.panel != captured.Panel || !identityMatches)
            {
                Consume(evt);
            }
            return true;
        }

        public int ClearPending()
        {
            int count = _forwarded.Count;
            _forwarded.Clear();
            return count;
        }

        public bool HandleFocusOrButton(KeyDownEvent evt, DataVisualizerInputSettings input)
        {
            if (IsRouting || CatalogInputSettings.IsTextEntry(evt))
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
                return MoveFocus(evt, false);
            }
            if (input.previousFocus.Matches(evt))
            {
                return MoveFocus(evt, true);
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
            if (IsRouting || CatalogInputSettings.IsTextEntry(evt) || evt.target is VisualElement target && (target is Button || target.GetFirstAncestorOfType<Button>() != null))
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

        public bool HandleButtonPointer(EventBase evt, int button)
        {
            if (evt.target is VisualElement target && (target is Button || target.GetFirstAncestorOfType<Button>() != null))
            {
                return HandlePointer(evt, button);
            }
            return false;
        }

        public bool HandleListPointer(EventBase evt, DataVisualizerInputSettings input)
        {
            IPointerEvent pointer = (IPointerEvent)evt;
            EventModifiers range = input.rangeSelectionModifiers;
            bool extend = range != EventModifiers.None && (pointer.modifiers & range) == range;
            EventModifiers remaining = extend ? pointer.modifiers & ~range : pointer.modifiers;
            EventModifiers native = extend ? EventModifiers.Shift : EventModifiers.None;
            if (input.toggleSelection.MatchesModifiers(remaining))
            {
                native |= Application.platform == RuntimePlatform.OSXEditor ? EventModifiers.Command : EventModifiers.Control;
            }
            return HandlePointer(evt, input.Navigation.PointerButton, native);
        }

        public bool HandlePointer(EventBase evt, int button, EventModifiers? modifiers = null)
        {
            if (AcceptForwarded(evt) || IsRouting)
            {
                return false;
            }
            IPointerEvent pointer = (IPointerEvent)evt;
            EventModifiers nativeModifiers = modifiers ?? pointer.modifiers;
            if (button == 0 && nativeModifiers == pointer.modifiers)
            {
                return false;
            }
            if (pointer.button != button)
            {
                if (pointer.button == 0)
                {
                    Consume(evt);
                    return true;
                }
                return false;
            }
            if (evt.target is not VisualElement target)
            {
                return false;
            }
            using ReusableDisposalLease<NativeInputRouter> scope = _scope.Acquire(this);
            IsRouting = true;
            Event mouse = new()
            {
                type = evt is PointerDownEvent ? EventType.MouseDown : EventType.MouseUp,
                button = 0,
                mousePosition = pointer.position,
                modifiers = nativeModifiers,
                clickCount = pointer.clickCount,
            };
            if (evt is PointerDownEvent)
            {
                using PointerDownEvent forwarded = PointerDownEvent.GetPooled(mouse);
                forwarded.target = target;
                _forwarded.Add(forwarded, CaptureIdentity(target, pointer.position));
                target.SendEvent(forwarded);
            }
            else
            {
                using PointerUpEvent forwarded = PointerUpEvent.GetPooled(mouse);
                forwarded.target = target;
                _forwarded.Add(forwarded, CaptureIdentity(target, pointer.position));
                target.SendEvent(forwarded);
            }
            Consume(evt);
            return true;
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
                using KeyUpEvent forwarded = KeyUpEvent.GetPooled(NativeCharacter(key), key, modifiers);
                forwarded.target = target;
                target.SendEvent(forwarded);
            }
            else
            {
                using KeyDownEvent forwarded = KeyDownEvent.GetPooled(NativeCharacter(key), key, modifiers);
                forwarded.target = target;
                target.SendEvent(forwarded);
            }
            Consume(evt);
            return true;
        }
    }
}
