namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using System;
    using Styles;
    using UnityEngine.UIElements;

    public sealed class ActionButtonToggle : VisualElement, INotifyValueChanged<bool>
    {
        public const string ClassName = "action-button-toggle";
        public const string LabelClassName = ClassName + "__label";
        public const string ButtonClassName = ClassName + "__button";
        public const string KnobClassName = ClassName + "__knob";
        public const string ButtonOnClassName = ButtonClassName + "--on";

        public bool value
        {
            get => _value;
            set
            {
                if (_value == value)
                {
                    return;
                }

                using ChangeEvent<bool> evt = ChangeEvent<bool>.GetPooled(_value, value);
                evt.target = this;
                SetValueWithoutNotify(value);
                SendEvent(evt);
            }
        }

        public string Label
        {
            get => _labelElement?.text;
            set
            {
                if (_labelElement != null)
                {
                    _labelElement.text = value;
                }
            }
        }

        private readonly Label _labelElement;
        private readonly Button _toggleButtonElement;
        private readonly Action<bool> _onClick;
        private readonly Func<DataVisualizerInputSettings> _getInput;
        private readonly DataVisualizerInputSettings _defaultInput = new();

        private bool _value;

        public ActionButtonToggle(string label, Action<bool> onClick = null, bool initialValue = false, Func<DataVisualizerInputSettings> getInput = null)
        {
            _onClick = onClick;
            _getInput = getInput;
            AddToClassList(ClassName);
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;

            _labelElement = new Label(label);
            _labelElement.AddToClassList(LabelClassName);
            _labelElement.pickingMode = PickingMode.Ignore;
            Add(_labelElement);

            // The owning window routes configured input before this native button handles it.
            _toggleButtonElement = new Button(ToggleValue);
            _toggleButtonElement.AddToClassList(ButtonClassName);
            _toggleButtonElement.RegisterCallback<TooltipEvent>(evt =>
            {
                CatalogInputSettings input = (_getInput?.Invoke() ?? _defaultInput).Navigation;
                evt.tooltip = $"{(Label ?? string.Empty).TrimEnd(' ', ':')} ({input.SelectKey}/{input.ToggleKey})";
                evt.rect = _toggleButtonElement.worldBound;
                evt.StopImmediatePropagation();
            });
            _toggleButtonElement.RegisterCallback<PointerDownEvent>(static evt => evt.StopPropagation());
            Add(_toggleButtonElement);
            _toggleButtonElement.AddToClassList(StyleConstants.ClickableClass);

            VisualElement knobElement = new();
            knobElement.AddToClassList(KnobClassName);
            knobElement.pickingMode = PickingMode.Ignore;
            _toggleButtonElement.Add(knobElement);

            SetValueWithoutNotify(initialValue);
        }

        public void SetValueWithoutNotify(bool newValue)
        {
            _value = newValue;
            UpdateVisualState();
        }

        private void ToggleValue()
        {
            value = !value;
            _onClick?.Invoke(value);
        }

        private void UpdateVisualState()
        {
            _toggleButtonElement?.EnableInClassList(ButtonOnClassName, _value);
        }
    }
}
