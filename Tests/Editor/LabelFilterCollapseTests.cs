namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using WallstopStudios.DataVisualizer.Editor.Utilities;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;

    public sealed class LabelFilterCollapseTests
    {
        private static T ReadPrivateField<T>(DataVisualizerWindow window, string fieldName)
        {
            FieldInfo field = typeof(DataVisualizerWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(field != null, $"The {fieldName} field must exist.");
            return (T)field.GetValue(window);
        }

        private static void SetPrivateField(DataVisualizerWindow window, string fieldName, object value)
        {
            FieldInfo field = typeof(DataVisualizerWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(field != null, $"The {fieldName} field must exist.");
            field.SetValue(window, value);
        }

        private static object InvokePrivate(DataVisualizerWindow window, string methodName, params object[] arguments)
        {
            MethodInfo method = typeof(DataVisualizerWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(method != null, $"The {methodName} method must exist.");
            return method.Invoke(window, arguments);
        }

        private static void EnsureFolderExists(string folder)
        {
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[index]);
                }

                current = next;
            }
        }

        private static void CreateFixtureAsset(string folder, string assetName, params string[] labels)
        {
            string assetPath = folder + "/" + assetName;
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<TestDataObject>(), assetPath);
            if (0 < labels.Length)
            {
                AssetDatabase.SetLabels(AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath), labels);
            }

            AssetDatabase.SaveAssets();
        }

        private static IEnumerator WaitForRefresh(DataVisualizerWindow window)
        {
            AssetChangeRefreshService service = ReadPrivateField<AssetChangeRefreshService>(window, "_assetChanges");
            for (int tick = 0; tick < 3000; tick++)
            {
                if (
                    window.IsSearchIndexComplete
                    && AssetGuidTypeIndex.Shared.IsComplete
                    && service.PendingCount == 0
                    && !ReadPrivateField<bool>(window, "_isLoadingObjectsAsync")
                    && !ReadPrivateField<bool>(window, "_needsRefresh")
                )
                {
                    yield break;
                }
                yield return null;
            }

            Assert.Fail("Asset changes did not settle.");
        }

        private static DataVisualizerWindow OpenLabelledWindow(TestCleanupScope cleanup, Type type, out TypeLabelFilterConfig config)
        {
            string folder = "Assets/DataVisualizerLabelCollapseTests_" + Guid.NewGuid().ToString("N");
            EnsureFolderExists(folder);
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            CreateFixtureAsset(folder, "Urgent.asset", "Urgent");
            CreateFixtureAsset(folder, "Plain.asset");

            DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            DataVisualizerUserState userState = new();
            userState.managedTypeNames.Add(type.FullName);
            string output = "Temp/LabelFilterCollapse-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() => System.IO.Directory.Delete(output, recursive: true));
            PerfWindowStateScope state = new(settings, userState, output);
            cleanup.Defer(() => state.Dispose());
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));

            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            SetPrivateField(window, "_settings", state.Settings);
            SetPrivateField(window, "_userState", state.UserState);
            SetPrivateField(window, "_userStateFilePath", state.FilePath);
            EditorSurfaceCapture.ShowPopup(window);
            SetPrivateField(window, "_settings", state.Settings);
            SetPrivateField(window, "_userState", state.UserState);
            SetPrivateField(window, "_userStateFilePath", state.FilePath);
            window.position = new Rect(40f, 40f, 1180f, 780f);
            window.CreateGUI();
            InvokePrivate(window, "LoadInitialContent");
            ReadPrivateField<NamespaceController>(window, "_namespaceController").SelectType(window, type);
            config = (TypeLabelFilterConfig)InvokePrivate(window, "LoadOrCreateLabelFilterConfig", type);
            return window;
        }

        [UnityTest]
        public IEnumerator ShouldKeepTheActiveFilterVisibleWhenLabelControlsCollapse()
        {
            using TestCleanupScope cleanup = new();
            Type type = typeof(TestDataObject);
            DataVisualizerWindow window = OpenLabelledWindow(cleanup, type, out TypeLabelFilterConfig config);
            yield return WaitForRefresh(window);

            config.andLabels.Clear();
            config.orLabels.Clear();
            config.andLabels.Add("Urgent");
            InvokePrivate(window, "ApplyLabelFilter", true);
            yield return WaitForRefresh(window);
            EditorSurfaceCapture.SettleLayout(window);

            Button toggle = window.rootVisualElement.Q<Button>("label-filter-collapse-toggle");
            Assert.That(toggle != null, "The Labels header needs a collapse toggle.");
            Assert.That(toggle.enabledSelf, "The Labels header collapses while a filter is active.");

            InvokePrivate(window, "ToggleLabelsCollapsed", true);
            yield return null;
            EditorSurfaceCapture.SettleLayout(window);

            Label summary = window.rootVisualElement.Q<Label>("label-filter-header-summary");
            Assert.That(summary != null, "A collapsed filter keeps a header summary.");
            Assert.That(summary.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(summary.text, Does.Contain("Urgent"), "The summary names the active filter.");
            int filtered = ReadPrivateField<List<ScriptableObject>>(window, "_filteredObjects").Count;
            int selected = ReadPrivateField<List<ScriptableObject>>(window, "_selectedObjects").Count;
            Assert.That(filtered, Is.EqualTo(1), "Only the labelled fixture matches.");
            Assert.That(summary.text, Does.Contain($"{filtered} shown · {selected - filtered} hidden"), "The summary keeps the neutral shown/hidden counts.");
            Rect column = window.rootVisualElement.Q("object-column").worldBound;
            Assert.That(summary.worldBound.xMax, Is.LessThanOrEqualTo(column.xMax + 0.5f), "The summary stays inside the object column.");
            Assert.That(summary.worldBound.height, Is.Positive);

            VisualElement editor = window.rootVisualElement.Q("label-filter-editor");
            Assert.That(editor.resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "Collapsing hides the filter controls.");
            ListView list = window.rootVisualElement.Q<ListView>("object-listview");
            Assert.That(list.itemsSource.Count, Is.EqualTo(1), "The collapsed filter still narrows the object list.");
            Assert.That(((ScriptableObject)list.itemsSource[0]).name, Is.EqualTo("Urgent"));

            InvokePrivate(window, "ToggleLabelsCollapsed", false);
            yield return null;
            EditorSurfaceCapture.SettleLayout(window);
            Assert.That(summary.resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "Expanded controls carry the summary themselves.");
            Assert.That(editor.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
        }

        /*
            The Labels controls collapse to one header row, so an empty object list that only
            names the type leaves no visible place to fix the filter that hides every row.
            Name the filter labels and where to clear or edit them.
        */
        [UnityTest]
        public IEnumerator ShouldNameTheHiddenFilterInTheEmptyObjectList()
        {
            using TestCleanupScope cleanup = new();
            Type type = typeof(TestDataObject);
            DataVisualizerWindow window = OpenLabelledWindow(cleanup, type, out TypeLabelFilterConfig config);
            yield return WaitForRefresh(window);

            config.andLabels.Clear();
            config.orLabels.Clear();
            config.andLabels.Add("Missing");
            InvokePrivate(window, "ApplyLabelFilter", true);
            yield return WaitForRefresh(window);
            EditorSurfaceCapture.SettleLayout(window);

            Label empty = window.rootVisualElement.Q<Label>("empty-object-list-label");
            ListView list = window.rootVisualElement.Q<ListView>("object-listview");
            Assert.That(empty.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(list.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(empty.text, Does.Contain("Missing"), "The message names the filter labels that hide every object.");
            Assert.That(empty.text, Does.Contain("Labels"), "The message names where to clear or edit the filter.");
        }

        [UnityTest]
        public IEnumerator ShouldHideTheHeaderSummaryWhenNoFilterIsActive()
        {
            using TestCleanupScope cleanup = new();
            Type type = typeof(TestDataObject);
            DataVisualizerWindow window = OpenLabelledWindow(cleanup, type, out TypeLabelFilterConfig config);
            yield return WaitForRefresh(window);

            config.andLabels.Clear();
            config.orLabels.Clear();
            InvokePrivate(window, "ApplyLabelFilter", true);
            InvokePrivate(window, "ToggleLabelsCollapsed", true);
            yield return null;
            EditorSurfaceCapture.SettleLayout(window);

            Label summary = window.rootVisualElement.Q<Label>("label-filter-header-summary");
            Assert.That(summary != null, "A collapsed filter keeps a header summary.");
            Assert.That(summary.resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "An unfiltered collapse needs no summary.");
        }
    }
}
