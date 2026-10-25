namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using System;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>Distinguishes explicit deselection gestures from incidental empty ListView notifications.</summary>
    public sealed class ObjectListSelectionInput : IDisposable
    {
        public bool IsRouting => _native.IsRouting;
        public bool AllowsEmptySelection { get; private set; }
        public DataVisualizerInputSettings Input => _getInput?.Invoke() ?? _defaults;

        private readonly DataVisualizerInputSettings _defaults = new();
        private readonly Func<DataVisualizerInputSettings> _getInput;
        private readonly NativeInputRouter _native = new();
        private readonly ListView _list;
        private readonly VisualElement _root;
        private IVisualElementScheduledItem _reset;

        public ObjectListSelectionInput(ListView list, VisualElement root, Func<DataVisualizerInputSettings> getInput = null)
        {
            _getInput = getInput;
            _list = list ?? throw new ArgumentNullException(nameof(list));
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _root.RegisterCallback<PointerDownEvent>(BeginPointer, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerUpEvent>(BeginPointer, TrickleDown.TrickleDown);
            _root.RegisterCallback<KeyDownEvent>(BeginKey, TrickleDown.TrickleDown);
            _root.RegisterCallback<KeyUpEvent>(BeginKey, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerCancelEvent>(CancelInput, TrickleDown.TrickleDown);
        }

        private static bool IsButton(object target)
        {
            return target is VisualElement element && (element is Button || element.GetFirstAncestorOfType<Button>() != null);
        }

        public void BlockEmptySelection()
        {
            SetInputState(false);
        }

        public void Dispose()
        {
            SetInputState(false);
            _native.ClearPending();
            _root.UnregisterCallback<PointerDownEvent>(BeginPointer, TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerUpEvent>(BeginPointer, TrickleDown.TrickleDown);
            _root.UnregisterCallback<KeyDownEvent>(BeginKey, TrickleDown.TrickleDown);
            _root.UnregisterCallback<KeyUpEvent>(BeginKey, TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerCancelEvent>(CancelInput, TrickleDown.TrickleDown);
        }

        private void BeginPointer(EventBase evt)
        {
            bool forwarded = _native.AcceptForwarded(evt);
            if (evt.isPropagationStopped || (!forwarded && IsRouting) || evt.target is not VisualElement target || (target != _list && !_list.Contains(target)))
            {
                return;
            }
            IPointerEvent pointer = (IPointerEvent)evt;
            bool rowTarget = false;
            for (VisualElement element = evt.target as VisualElement; element != null && element != _list; element = element.parent)
            {
                if (element.userData is ScriptableObject)
                {
                    rowTarget = true;
                    break;
                }
            }
            SetInputState(
                !IsButton(evt.target)
                    && (
                        forwarded
                            ? pointer.actionKey || !rowTarget
                            : pointer.button == Input.Navigation.PointerButton && (Input.toggleSelection.MatchesModifiers(pointer.modifiers) || !rowTarget)
                    )
            );
            if (!forwarded && !IsButton(evt.target))
            {
                _native.HandleListPointer(evt, Input);
            }
        }

        private void BeginKey(EventBase evt)
        {
            if (IsRouting || evt.target is not VisualElement target || (target != _list && !_list.Contains(target)))
            {
                return;
            }
            IKeyboardEvent key = (IKeyboardEvent)evt;
            SetInputState(!IsButton(evt.target) && Input.toggleSelection.Matches(key));
            _native.HandleListKey(evt, Input);
        }

        private void CancelInput(EventBase evt)
        {
            SetInputState(false);
        }

        private void ClearInput()
        {
            SetInputState(false);
        }

        private void SetInputState(bool allow)
        {
            _reset?.Pause();
            _reset = null;
            AllowsEmptySelection = allow;
            if (allow)
            {
                // Input authorization ends after this dispatch, even if mouse capture bypasses release callbacks.
                _reset = _root.schedule.Execute(ClearInput);
            }
        }
    }
}
