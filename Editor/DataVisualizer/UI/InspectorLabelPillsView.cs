namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor.Styles;

    /*
        Label pills for the selected asset in the inspector pane. Labels reconcile by
        ordinal text instead of clearing the row, so a label edit keeps every surviving
        remove button alive and keyboard focus does not fall back to the window body.
        LabelFilterEditor uses the same refresh shape for its filter pills; the inspector
        copy previously rebuilt the row and lost the focused button (#308).
    */
    public sealed class InspectorLabelPillsView : VisualElement
    {
        public const string EmptyMessage = "No labels assigned.";

        private readonly Func<string, Color> _colorForLabel;
        private readonly Func<VisualElement> _fallbackFocus;
        private readonly Action<string> _remove;
        private Label _emptyLabel;

        public InspectorLabelPillsView(Action<string> remove, Func<string, Color> colorForLabel, Func<VisualElement> fallbackFocus)
        {
            name = "inspector-current-labels";
            AddToClassList("label-pill-container");
            style.marginRight = 6;
            style.marginLeft = 6;
            _remove = remove;
            _colorForLabel = colorForLabel;
            _fallbackFocus = fallbackFocus;
        }

        public static bool IsDarkBackground(Color color)
        {
            return 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b < 0.5f; // Luminance check
        }

        private static List<string> CollectOrderedLabels(IReadOnlyList<string> labels)
        {
            List<string> ordered = new(labels?.Count ?? 0);
            if (labels == null)
            {
                return ordered;
            }
            int count = labels.Count;
            for (int labelIndex = 0; labelIndex < count; labelIndex++)
            {
                string labelText = labels[labelIndex];
                if (string.IsNullOrWhiteSpace(labelText))
                {
                    continue;
                }
                ordered.Add(labelText);
            }
            ordered.Sort(StringComparer.Ordinal);
            return ordered;
        }

        public void Bind(IReadOnlyList<string> labels)
        {
            VisualElement focusedElement = panel?.focusController?.focusedElement as VisualElement;
            bool focusInside = focusedElement != null && Contains(focusedElement);
            _emptyLabel?.RemoveFromHierarchy();
            List<string> orderedLabels = CollectOrderedLabels(labels);
            int index = 0;
            int focusReplacementIndex = -1;
            string previousLabel = null;
            foreach (string labelText in orderedLabels)
            {
                if (string.Equals(previousLabel, labelText, StringComparison.Ordinal))
                {
                    continue;
                }
                previousLabel = labelText;
                while (index < childCount && StringComparer.Ordinal.Compare(LabelAt(index), labelText) < 0)
                {
                    if (RemovePill(index, focusedElement))
                    {
                        focusReplacementIndex = index;
                    }
                }
                if (index < childCount && string.Equals(LabelAt(index), labelText, StringComparison.Ordinal))
                {
                    index++;
                    continue;
                }
                Insert(index, CreatePill(labelText));
                index++;
            }
            while (index < childCount)
            {
                if (RemovePill(index, focusedElement))
                {
                    focusReplacementIndex = index;
                }
            }
            if (0 < childCount)
            {
                if (focusInside && focusedElement.panel == null)
                {
                    FocusRemoveButton(focusReplacementIndex < 0 ? 0 : Math.Min(focusReplacementIndex, childCount - 1));
                }
                return;
            }
            ShowEmptyState();
            if (focusInside)
            {
                _fallbackFocus?.Invoke()?.Focus();
            }
        }

        private VisualElement CreatePill(string labelText)
        {
            Color backgroundColor = _colorForLabel != null ? _colorForLabel(labelText) : Color.gray;
            VisualElement pillContainer = new()
            {
                name = $"inspector-label-pill-{labelText.Replace(" ", "-").ToLowerInvariant()}",
                style = { backgroundColor = backgroundColor },
                userData = labelText,
            };
            pillContainer.AddToClassList("label-pill");
            pillContainer.AddToClassList("non-draggable");

            Color textColor = IsDarkBackground(backgroundColor) ? Color.white : Color.black;
            Label labelElement = new(labelText) { style = { color = textColor } };
            labelElement.AddToClassList("label-pill-text");
            pillContainer.Add(labelElement);

            Button removeButton = new(() => _remove?.Invoke(labelText)) { text = "x", tooltip = $"Remove label '{labelText}'" };
            removeButton.AddToClassList(StyleConstants.ClickableClass);
            removeButton.AddToClassList("label-pill-remove-button");
            removeButton.style.color = textColor;
            pillContainer.Add(removeButton);
            return pillContainer;
        }

        private string LabelAt(int index)
        {
            return this[index].userData as string;
        }

        private bool RemovePill(int index, VisualElement focusedElement)
        {
            bool removedFocus = focusedElement != null && this[index].Contains(focusedElement);
            RemoveAt(index);
            return removedFocus;
        }

        private void FocusRemoveButton(int index)
        {
            if (index < childCount)
            {
                Button removeButton = this[index].Q<Button>(className: "label-pill-remove-button");
                if (removeButton != null)
                {
                    removeButton.Focus();
                    return;
                }
            }
            _fallbackFocus?.Invoke()?.Focus();
        }

        private void ShowEmptyState()
        {
            if (_emptyLabel == null)
            {
                _emptyLabel = new Label(EmptyMessage) { style = { fontSize = 10, unityFontStyleAndWeight = FontStyle.Italic } };
                _emptyLabel.AddToClassList("dataviz-muted");
            }
            if (_emptyLabel.parent != this)
            {
                Add(_emptyLabel);
            }
        }
    }
}
