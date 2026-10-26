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
    using WallstopStudios.DataVisualizer.Editor.Data;
    using WallstopStudios.DataVisualizer.Editor.Styles;
    using WallstopStudios.DataVisualizer.Editor.UI;

    public sealed class LabelFilterEditorTests
    {
        private static IEnumerable<TestCaseData> SavedFilters()
        {
            foreach (int combination in new[] { 0, 1, 2 })
            {
                for (int clauses = 0; clauses < 4; clauses++)
                {
                    yield return new TestCaseData(combination, clauses).SetName($"SavedFilterCombination{combination}Clauses{clauses}");
                }
            }
        }

        private static Button Pill(VisualElement editor, string label)
        {
            foreach (VisualElement element in editor.Q("simple-label-pills").Children())
            {
                if (element is Button button && string.Equals(button.userData as string, label, StringComparison.Ordinal))
                {
                    return button;
                }
            }
            Assert.Fail("Missing label pill: " + label);
            return null;
        }

        private static void Submit(Button button)
        {
            button.Focus();
            using NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }

        [UnityTest]
        public IEnumerator ShouldPreserveLabelControlsAndFocusAcrossRefreshes()
        {
            using TestCleanupScope cleanup = new();
            EditorSurfaceCaptureHostWindow window = ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            window.position = new Rect(80, 80, 360, 300);
            EditorSurfaceCapture.ShowPopup(window);
            int saves = 0;
            TypeLabelFilterConfig config = new();
            LabelFilterEditor editor = new(new VisualElement(), () => saves++);
            window.rootVisualElement.Add(editor);
            editor.Bind(config, new[] { "Armor", "Urgent", "urgent" });
            yield return null;
            EditorSurfaceCapture.SettleLayout(window);
            Button urgent = Pill(editor, "Urgent");
            Button lower = Pill(editor, "urgent");
            urgent.Focus();
            yield return null;
            editor.Bind(config, new[] { "Armor", "Urgent", "urgent" });
            Assert.That(Pill(editor, "Urgent"), Is.SameAs(urgent), "An asset refresh must reuse unchanged label controls.");
            Assert.That(window.rootVisualElement.focusController.focusedElement, Is.SameAs(urgent));
            editor.Bind(config, new[] { "Urgent", "Before", "urgent", "After", "Urgent" });
            Assert.That(Pill(editor, "Urgent"), Is.SameAs(urgent), "Insertions and removals preserve surviving identities.");
            Assert.That(Pill(editor, "urgent"), Is.SameAs(lower), "Label identity is ordinal and case-sensitive.");
            Assert.That(editor.Q("simple-label-pills").childCount, Is.EqualTo(4), "Each exact label has one control.");
            Assert.That(window.rootVisualElement.focusController.focusedElement, Is.SameAs(urgent));
            config.andLabels.Add("Urgent");
            editor.Bind(config, new[] { "After", "urgent" });
            Assert.That(Pill(editor, "Urgent"), Is.SameAs(urgent), "Missing selected labels remain removable.");
            Assert.That(urgent.text, Is.EqualTo("Urgent ×"));
            Assert.That(urgent.ClassListContains(StyleConstants.SelectedClass));
            Submit(urgent);
            Assert.That(config.andLabels, Is.Empty);
            Assert.That(saves, Is.EqualTo(1));
            Assert.That(editor.Q("simple-label-pills").childCount, Is.EqualTo(2));
            Assert.That(
                window.rootVisualElement.focusController.focusedElement,
                Is.SameAs(editor.Q<DropdownField>("label-filter-mode")),
                "Removing the focused label returns focus to the mode control."
            );
            Assert.That(Pill(editor, "urgent"), Is.SameAs(lower));
            editor.Q<DropdownField>("label-filter-mode").value = LabelFilterEditor.AdvancedMode;
            Assert.That(editor.Q("simple-label-pills").childCount, Is.Zero, "Hidden advanced mode must not retain obsolete pill controls.");
            editor.Q<DropdownField>("label-filter-mode").value = LabelFilterEditor.AnyMode;
            Assert.That(Pill(editor, "urgent").text, Is.EqualTo("urgent +"));
            editor.Bind(null, Array.Empty<string>());
            Assert.That(editor.Q("simple-label-pills").childCount, Is.Zero);
            Assert.That(saves, Is.EqualTo(1), "Presentation refreshes never persist filter changes.");
        }

        [Test, Explicit("Repeated label rendering component; creates no assets or native windows")]
        public void ShouldMeasureRepeatedLabelBinding()
        {
            BenchmarkCodeIdentity production = new(
                typeof(LabelFilterEditor).Assembly,
                "Packages/com.wallstop-studios.data-visualizer/Editor/DataVisualizer/UI/LabelFilterEditor.cs"
            );
            BenchmarkCodeIdentity driver = new(typeof(LabelFilterEditorTests).Assembly, "Packages/com.wallstop-studios.data-visualizer/Tests/Editor/LabelFilterEditorTests.cs");
            production.Verify();
            driver.Verify();
            List<string> output = new()
            {
                "# Unity=" + Application.unityVersion + "; OS=" + SystemInfo.operatingSystem + "; CPU=" + SystemInfo.processorType,
                "# productionSource=" + production.SourceSha256 + "; module=" + production.LoadedModuleId,
                "# driverSource=" + driver.SourceSha256 + "; module=" + driver.LoadedModuleId,
                "labels,sample,milliseconds,replacedControls",
            };
            foreach (int count in new[] { 10, 100, 1000 })
            {
                string[] labels = new string[count];
                for (int i = 0; i < count; i++)
                {
                    labels[i] = "Label" + i.ToString("D4");
                }
                LabelFilterEditor editor = new(new VisualElement(), () => Assert.Fail("Binding cannot save."));
                TypeLabelFilterConfig config = new();
                editor.Bind(config, labels);
                VisualElement pills = editor.Q("simple-label-pills");
                VisualElement[] previous = new VisualElement[count];
                for (int sample = -5; sample < 30; sample++)
                {
                    for (int i = 0; i < count; i++)
                    {
                        previous[i] = pills[i];
                    }
                    long start = System.Diagnostics.Stopwatch.GetTimestamp();
                    editor.Bind(config, labels);
                    double milliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    int replaced = 0;
                    for (int i = 0; i < count; i++)
                    {
                        if (!ReferenceEquals(previous[i], pills[i]))
                        {
                            replaced++;
                        }
                    }
                    if (0 <= sample)
                    {
                        output.Add(count + "," + sample + "," + milliseconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," + replaced);
                    }
                }
            }
            production.Verify();
            driver.Verify();
            output.Add("# status=complete; five discarded warmups and thirty samples per label count; unhosted controls, no asset or whole-editor timings");
            System.IO.File.WriteAllText("Temp/label-binding.csv", string.Join("\n", output) + "\n");
        }

        [TestCaseSource(nameof(SavedFilters))]
        public void ShouldPreserveSavedExpressionsWhenBindingAndOpeningAdvanced(int combination, int clauses)
        {
            TypeLabelFilterConfig config = new()
            {
                combinationType = (LabelCombinationType)combination,
                andLabels = clauses % 2 == 1 ? new List<string> { "Urgent", "urgent", "Missing" } : new(),
                orLabels = 2 <= clauses ? new List<string> { "Armor", "Weapon" } : new(),
                isCollapsed = true,
                isAdvancedCollapsed = true,
            };
            using TestCleanupScope cleanup = new();
            EditorSurfaceCaptureHostWindow window = ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            EditorSurfaceCapture.ShowPopup(window);
            string saved = JsonUtility.ToJson(config);
            LabelFilterEditor editor = new(new VisualElement(), () => Assert.Fail("Presentation must not write saved filters."));
            window.rootVisualElement.Add(editor);
            editor.Bind(JsonUtility.FromJson<TypeLabelFilterConfig>(saved), new[] { "Urgent", "urgent", "Armor" });
            DropdownField mode = editor.Q<DropdownField>("label-filter-mode");
            string expected =
                clauses == 3 ? LabelFilterEditor.AdvancedMode
                : clauses == 2 ? LabelFilterEditor.AnyMode
                : LabelFilterEditor.AllMode;
            Assert.That(mode.value, Is.EqualTo(expected));
            editor.Bind(config, Array.Empty<string>());
            mode.value = LabelFilterEditor.AdvancedMode;
            editor.Bind(config, Array.Empty<string>());
            Assert.That(mode.value, Is.EqualTo(LabelFilterEditor.AdvancedMode));
            Assert.That(JsonUtility.ToJson(config), Is.EqualTo(saved));
            if (clauses == 3)
            {
                mode.value = LabelFilterEditor.AllMode;
                Assert.That(mode.value, Is.EqualTo(LabelFilterEditor.AdvancedMode));
                Assert.That(JsonUtility.ToJson(config), Is.EqualTo(saved));
                Assert.That(editor.Q<Label>("label-filter-summary").text, Does.Contain("Missing"));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShouldKeepAdvancedWhileEditingAnExistingCompoundFilter(bool attemptSimpleMode)
        {
            using TestCleanupScope cleanup = new();
            EditorSurfaceCaptureHostWindow window = ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            EditorSurfaceCapture.ShowPopup(window);
            TypeLabelFilterConfig config = new()
            {
                andLabels = new() { "Reviewed" },
                orLabels = new() { "Urgent" },
            };
            VisualElement advanced = new();
            LabelFilterEditor editor = new(advanced, () => Assert.Fail("Changing presentation must not write filter state."));
            window.rootVisualElement.Add(editor);
            editor.Bind(config, new[] { "Reviewed", "Urgent" });
            DropdownField mode = editor.Q<DropdownField>("label-filter-mode");
            if (attemptSimpleMode)
            {
                mode.value = LabelFilterEditor.AnyMode;
            }
            config.orLabels.Clear();
            editor.Bind(config, new[] { "Reviewed", "Urgent" });
            Assert.That(mode.value, Is.EqualTo(LabelFilterEditor.AdvancedMode));
            Assert.That(advanced.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            mode.value = LabelFilterEditor.AllMode;
            Assert.That(advanced.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator ShouldAddSwitchRemoveAndClearLabelsWithKeyboard()
        {
            using TestCleanupScope cleanup = new();
            EditorSurfaceCaptureHostWindow window = ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            EditorSurfaceCapture.ShowPopup(window);
            window.position = new Rect(0, 0, 280, 300);
            TypeLabelFilterConfig config = new();
            int saves = 0;
            LabelFilterEditor editor = new(new VisualElement(), () => saves++);
            window.rootVisualElement.Add(editor);
            editor.Bind(config, new[] { "Urgent", "urgent" });
            EditorSurfaceCapture.SettleLayout(window);

            Submit(Pill(editor, "Urgent"));
            yield return null;
            Assert.That(config.andLabels, Is.EqualTo(new[] { "Urgent" }));
            Assert.That(LabelFilterEvaluator.Matches(new[] { "urgent" }, config), Is.False);
            Assert.That(window.rootVisualElement.focusController.focusedElement, Is.SameAs(Pill(editor, "Urgent")));
            Submit(Pill(editor, "urgent"));
            yield return null;
            Assert.That(LabelFilterEvaluator.Matches(new[] { "Urgent" }, config), Is.False);

            editor.Q<DropdownField>("label-filter-mode").value = LabelFilterEditor.AnyMode;
            yield return null;
            Assert.That(config.andLabels, Is.Empty);
            Assert.That(config.orLabels, Is.EqualTo(new[] { "Urgent", "urgent" }));
            Assert.That(LabelFilterEvaluator.Matches(new[] { "Urgent" }, config), Is.True);
            Submit(Pill(editor, "Urgent"));
            yield return null;
            Assert.That(config.orLabels, Is.EqualTo(new[] { "urgent" }));
            Submit(editor.Q<Button>("clear-label-filter"));
            yield return null;
            Assert.That(config.andLabels, Is.Empty);
            Assert.That(config.orLabels, Is.Empty);
            Assert.That(saves, Is.EqualTo(5));
        }

        [UnityTest]
        public IEnumerator ShouldUseThemeColorsForNativeFieldValues()
        {
            using TestCleanupScope cleanup = new();
            DataVisualizerThemeSettings value = ScriptableObject.CreateInstance<DataVisualizerThemeSettings>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(value));
            value.name = "Une référence avec un nom long";
            EditorSurfaceCaptureHostWindow window = ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            EditorSurfaceCapture.ShowPopup(window);
            window.position = new Rect(80, 80, 360, 300);
            VisualElement root = window.rootVisualElement;
            root.AddToClassList("dataviz-root");
            const string styles = "Packages/com.wallstop-studios.data-visualizer/Editor/DataVisualizer/Styles/";
            root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(styles + "DataVisualizerStyles.uss"));
            DropdownField popup = new(new List<string> { "Match all labels", "Match any label" }, 0);
            UnityEditor.UIElements.ObjectField reference = new() { objectType = typeof(DataVisualizerThemeSettings), value = value };
            Label muted = new("Disabled value");
            muted.AddToClassList("dataviz-muted");
            root.Add(popup);
            root.Add(reference);
            root.Add(muted);
            DataVisualizerThemeSelection selection = new();
            cleanup.Defer(() => selection.Apply(null, null));
            foreach (string theme in new[] { "Dx", "Classic", "Nord", "Dracula", "Compact", "Minimal" })
            {
                selection.Apply(root, AssetDatabase.LoadAssetAtPath<DataVisualizerThemeSettings>(styles + theme + ".asset"));
                popup.SetEnabled(true);
                reference.SetEnabled(true);
                yield return null;
                EditorSurfaceCapture.SettleLayout(window);
                TextElement popupText = popup.Q<TextElement>(className: DropdownField.textUssClassName);
                VisualElement popupArrow = popup.Q(className: DropdownField.arrowUssClassName);
                VisualElement objectDisplay = reference.Q(className: UnityEditor.UIElements.ObjectField.objectUssClassName);
                TextElement objectText = objectDisplay as TextElement ?? objectDisplay.Q<TextElement>();
                Assert.That(popupText != null && objectText != null, theme);
                double activeDeadline = EditorApplication.timeSinceStartup + 2;
                while (
                    (
                        !popupText.resolvedStyle.color.Equals(root.resolvedStyle.color)
                        || !objectText.resolvedStyle.color.Equals(root.resolvedStyle.color)
                        || !popupArrow.resolvedStyle.unityBackgroundImageTintColor.Equals(root.resolvedStyle.color)
                    )
                    && EditorApplication.timeSinceStartup < activeDeadline
                )
                {
                    yield return null;
                    EditorSurfaceCapture.SettleLayout(window);
                }
                Assert.That(popupText.resolvedStyle.color, Is.EqualTo(root.resolvedStyle.color), theme + " dropdown text follows the theme.");
                Assert.That(popupArrow.resolvedStyle.unityBackgroundImageTintColor, Is.EqualTo(root.resolvedStyle.color), theme + " dropdown arrow follows the theme.");
                Assert.That(objectText.resolvedStyle.color, Is.EqualTo(root.resolvedStyle.color), theme + " object text follows the theme.");
                popup.SetEnabled(false);
                reference.SetEnabled(false);
                yield return null;
                EditorSurfaceCapture.SettleLayout(window);
                double disabledDeadline = EditorApplication.timeSinceStartup + 2;
                while (
                    (
                        !popupText.resolvedStyle.color.Equals(muted.resolvedStyle.color)
                        || !objectText.resolvedStyle.color.Equals(muted.resolvedStyle.color)
                        || !popupArrow.resolvedStyle.unityBackgroundImageTintColor.Equals(muted.resolvedStyle.color)
                    )
                    && EditorApplication.timeSinceStartup < disabledDeadline
                )
                {
                    yield return null;
                    EditorSurfaceCapture.SettleLayout(window);
                }
                Assert.That(popupText.resolvedStyle.color, Is.EqualTo(muted.resolvedStyle.color), theme + " disabled dropdown text stays readable.");
                Assert.That(popupArrow.resolvedStyle.unityBackgroundImageTintColor, Is.EqualTo(muted.resolvedStyle.color), theme + " disabled arrow follows the theme.");
                Assert.That(objectText.resolvedStyle.color, Is.EqualTo(muted.resolvedStyle.color), theme + " disabled object text stays readable.");
            }
        }

        [UnityTest]
        public IEnumerator ShouldFitSimpleControlsInNarrowPanesAcrossThemes()
        {
            using TestCleanupScope cleanup = new();
            EditorSurfaceCaptureHostWindow window = ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            EditorSurfaceCapture.ShowPopup(window);
            window.position = new Rect(0, 0, 220, 400);
            VisualElement root = window.rootVisualElement;
            root.AddToClassList("dataviz-root");
            const string styles = "Packages/com.wallstop-studios.data-visualizer/Editor/DataVisualizer/Styles/";
            root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(styles + "DataVisualizerStyles.uss"));
            LabelFilterEditor editor = new(new VisualElement(), () => { });
            root.Add(editor);
            editor.Bind(new TypeLabelFilterConfig(), new[] { "A label with a very long descriptive name", "Urgent" });
            DataVisualizerThemeSelection selection = new();
            cleanup.Defer(() => selection.Apply(null, null));
            foreach (string theme in new[] { "Dx", "Classic", "Nord", "Dracula", "Compact", "Minimal" })
            {
                selection.Apply(root, AssetDatabase.LoadAssetAtPath<DataVisualizerThemeSettings>(styles + theme + ".asset"));
                yield return null;
                EditorSurfaceCapture.SettleLayout(window);
                foreach (Button button in editor.Query<Button>().ToList())
                {
                    Assert.That(0 < button.worldBound.height, theme);
                    Assert.That(editor.worldBound.xMin <= button.worldBound.xMin, theme);
                    Assert.That(button.worldBound.xMax <= editor.worldBound.xMax + 1, theme);
                }
            }
        }

        [Test]
        public void ShouldKeepMissingActiveLabelsAvailableForRemoval()
        {
            TypeLabelFilterConfig config = new() { andLabels = new List<string> { "Missing" } };
            LabelFilterEditor editor = new(new VisualElement(), () => { });
            editor.Bind(config, Array.Empty<string>());
            Assert.That(Pill(editor, "Missing").text, Does.Contain("×"));
            Assert.That(editor.Q<Label>("label-filter-summary").text, Does.Contain("Missing"));
            Assert.That(LabelFilterEvaluator.Matches(Array.Empty<string>(), config), Is.False);
        }
    }
}
