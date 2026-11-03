namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor.UI;

    public sealed class InspectorLabelPillsViewTests
    {
        private static VisualElement Pill(InspectorLabelPillsView view, string label)
        {
            foreach (VisualElement element in view.Children())
            {
                if (string.Equals(element.userData as string, label, StringComparison.Ordinal))
                {
                    return element;
                }
            }
            Assert.Fail("Missing inspector label pill: " + label);
            return null;
        }

        private static Button RemoveButton(InspectorLabelPillsView view, string label)
        {
            Button button = Pill(view, label).Q<Button>(className: "label-pill-remove-button");
            Assert.That(button != null, "Every inspector label pill owns one remove button.");
            return button;
        }

        private static void Submit(Button button)
        {
            button.Focus();
            using NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }

        private static void AssertFocused(VisualElement host, VisualElement expected, string message)
        {
            Assert.That(host.panel.focusController.focusedElement, Is.SameAs(expected), message);
        }

        [UnityTest]
        public IEnumerator ShouldRetainSurvivingPillsAndFocusWhenInspectorLabelsChange()
        {
            using TestCleanupScope cleanup = new();
            EditorSurfaceCaptureHostWindow window = ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            window.position = new Rect(80, 80, 360, 300);
            EditorSurfaceCapture.ShowPopup(window);
            TextField fallback = new() { name = "fallback-focus" };
            window.rootVisualElement.Add(fallback);
            List<string> removed = new();
            InspectorLabelPillsView view = new(removed.Add, _ => Color.gray, () => fallback);
            window.rootVisualElement.Add(view);
            view.Bind(new[] { "Urgent", "Armor", "urgent", "Armor" });
            yield return null;
            EditorSurfaceCapture.SettleLayout(window);

            Assert.That(view.childCount, Is.EqualTo(3), "Exact labels bind once in ordinal order.");
            Assert.That(Pill(view, "Armor").Q<Label>(className: "label-pill-text").text, Is.EqualTo("Armor"), "Pills keep their label text.");
            VisualElement urgentPill = Pill(view, "Urgent");
            VisualElement lowerUrgentPill = Pill(view, "urgent");
            Button urgentButton = RemoveButton(view, "Urgent");
            urgentButton.Focus();
            yield return null;

            view.Bind(new[] { "Urgent", "urgent" });
            Assert.That(Pill(view, "Urgent"), Is.SameAs(urgentPill), "Surviving pills keep their identity.");
            Assert.That(Pill(view, "urgent"), Is.SameAs(lowerUrgentPill), "Label identity is ordinal and case-sensitive.");
            AssertFocused(window.rootVisualElement, urgentButton, "A removed neighbor must not move focus away from the surviving pill.");

            view.Bind(new[] { "Zeta", "alpha" });
            Assert.That(view.childCount, Is.EqualTo(2), "New labels replace the removed pills.");
            Assert.That(Pill(view, "Zeta") != null, "Ordinal order places uppercase labels first.");
            AssertFocused(window.rootVisualElement, RemoveButton(view, "Zeta"), "Removing the focused pill focuses the next surviving remove button.");

            view.Bind(new[] { "alpha" });
            Submit(RemoveButton(view, "alpha"));
            Assert.That(removed, Is.EqualTo(new[] { "alpha" }), "The remove button forwards the exact label.");
            yield return null;

            view.Bind(new[] { "alpha" });
            Assert.That(removed, Is.EqualTo(new[] { "alpha" }), "Binding alone never mutates labels.");
        }

        [UnityTest]
        public IEnumerator ShouldElideAndHintLabelsLongerThanThePill()
        {
            using TestCleanupScope cleanup = new();
            EditorSurfaceCaptureHostWindow window = ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            window.position = new Rect(80, 80, 360, 300);
            EditorSurfaceCapture.ShowPopup(window);
            window.rootVisualElement.styleSheets.Add(
                AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.wallstop-studios.data-visualizer/Editor/DataVisualizer/Styles/DataVisualizerStyles.uss")
            );
            InspectorLabelPillsView view = new(_ => { }, _ => Color.gray, () => null);
            window.rootVisualElement.Add(view);
            string longLabel = "release-candidate-verification-required-before-shipping-to-production-and-signed-off-by-the-environment-owner";
            view.Bind(new[] { "Reviewed", longLabel });
            yield return null;
            EditorSurfaceCapture.SettleLayout(window);

            Label label = Pill(view, longLabel).Q<Label>(className: "label-pill-text");
            Button removeButton = RemoveButton(view, longLabel);
            Vector2 intrinsic = label.MeasureTextSize(longLabel, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
            Assert.That(label.worldBound.width, Is.LessThan(intrinsic.x), "The fixture label must be wider than the pill lets it paint.");
            Assert.That(label.tooltip, Is.EqualTo(longLabel), "A label cut off by the pill must stay readable through its hint.");
            Assert.That(label.resolvedStyle.whiteSpace, Is.EqualTo(WhiteSpace.NoWrap), "Pill text must not wrap under the fixed pill height.");
            Assert.That(label.resolvedStyle.textOverflow, Is.EqualTo(TextOverflow.Ellipsis), "Pill text must end with an ellipsis when it does not fit.");
            Assert.That(label.isElided, Is.True, "The rendered label must report that it dropped text.");
            Assert.That(Pill(view, longLabel).worldBound.xMax, Is.LessThanOrEqualTo(view.worldBound.xMax + 0.5f), "A long pill must stay inside the label container.");
            Assert.That(removeButton.worldBound.width, Is.GreaterThan(0f), "The remove button must keep its size beside a long label.");
            Assert.That(removeButton.worldBound.xMax, Is.LessThanOrEqualTo(Pill(view, longLabel).worldBound.xMax + 0.5f), "The remove button must stay inside the pill.");
        }

        [UnityTest]
        public IEnumerator ShouldShowOneEmptyNoticeAndFocusFallbackWhenLastLabelIsRemoved()
        {
            using TestCleanupScope cleanup = new();
            EditorSurfaceCaptureHostWindow window = ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            window.position = new Rect(80, 80, 360, 300);
            EditorSurfaceCapture.ShowPopup(window);
            TextField fallback = new() { name = "fallback-focus" };
            window.rootVisualElement.Add(fallback);
            InspectorLabelPillsView view = new(_ => { }, _ => Color.gray, () => fallback);
            window.rootVisualElement.Add(view);
            view.Bind(new[] { "Armor" });
            yield return null;
            EditorSurfaceCapture.SettleLayout(window);
            RemoveButton(view, "Armor").Focus();
            yield return null;

            view.Bind(new string[0]);
            Assert.That(view.childCount, Is.EqualTo(1), "The empty state is the only remaining element.");
            Label emptyLabel = view.Q<Label>(className: "dataviz-muted");
            Assert.That(emptyLabel != null, "An unlabelled asset explains the empty state.");
            Assert.That(emptyLabel.text, Is.EqualTo(InspectorLabelPillsView.EmptyMessage));
            AssertFocused(window.rootVisualElement, fallback, "Removing the last pill returns focus to the add-label field.");

            view.Bind(new string[0]);
            Assert.That(view.childCount, Is.EqualTo(1), "Repeated binding reuses one empty notice.");
            Assert.That(view.Q<Label>(className: "dataviz-muted"), Is.SameAs(emptyLabel));

            view.Bind(new[] { "Armor", " " });
            Assert.That(view.childCount, Is.EqualTo(1), "Blank labels never create a pill.");
            Assert.That(Pill(view, "Armor") != null);
        }
    }
}
