namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using WallstopStudios.DataVisualizer.Editor.UI;
    using WallstopStudios.DataVisualizer.Editor.Utilities;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;

    /*
        The Objects header names the type it lists. Expanded layouts can read the highlighted
        catalog row, but a narrow single-pane layout shows only the Objects pane, so nothing
        else said which type the rows belonged to.
    */
    public sealed class ObjectHeaderTypeNameTests
    {
        private const string LongTypeTitle = "Un objet avec un nom très long pour vérifier l'en-tête des objets";

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

        private static void InvokePrivate(DataVisualizerWindow window, string methodName, params object[] arguments)
        {
            MethodInfo method = typeof(DataVisualizerWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(method != null, $"The {methodName} method must exist.");
            method.Invoke(window, arguments);
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

        private static DataVisualizerWindow OpenWindow(TestCleanupScope cleanup)
        {
            DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            string output = "Temp/ObjectHeaderTypeName-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() => System.IO.Directory.Delete(output, recursive: true));
            PerfWindowStateScope state = new(settings, new DataVisualizerUserState(), output);
            cleanup.Defer(() => state.Dispose());
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));

            FieldInfo instance = typeof(DataVisualizerWindow).GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = instance.GetValue(null);
            cleanup.Defer(() => instance.SetValue(null, previous));
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            SetPrivateField(window, "_settings", state.Settings);
            SetPrivateField(window, "_userState", state.UserState);
            SetPrivateField(window, "_userStateFilePath", state.FilePath);
            window.position = new Rect(40f, 40f, 1180f, 780f);
            window.CreateGUI();
            InvokePrivate(window, "LoadInitialContent");
            EditorSurfaceCapture.ShowPopup(window);
            return window;
        }

        /*
            Replaces the managed catalog with one type and selects it through the same
            controller method a catalog row calls. The window has already settled, so the
            project scan cannot replace this synthetic selection.
        */
        private static void SelectOnlyType(DataVisualizerWindow window, Type type)
        {
            Dictionary<string, List<Type>> managed = ReadPrivateField<Dictionary<string, List<Type>>>(window, "_scriptableObjectTypes");
            managed.Clear();
            managed[NamespaceController.GetNamespaceKey(type)] = new List<Type> { type };
            InvokePrivate(window, "BuildNamespaceView");
            ReadPrivateField<NamespaceController>(window, "_namespaceController").SelectType(window, type);
        }

        [Test]
        public void ShouldHideTheTypeNameWhenTheListHasNoType()
        {
            using TestCleanupScope cleanup = new();
            bool anotherWindowIsOpen = 0 < Resources.FindObjectsOfTypeAll<DataVisualizerWindow>().Length;
            cleanup.Defer(() =>
            {
                if (anotherWindowIsOpen && !AssetGuidTypeIndex.Shared.IsComplete)
                {
                    AssetGuidTypeIndex.Shared.Rebuild();
                }
            });
            FieldInfo instance = typeof(DataVisualizerWindow).GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = instance.GetValue(null);
            cleanup.Defer(() => instance.SetValue(null, previous));
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));
            window.CreateGUI();

            Label typeName = window.rootVisualElement.Q<Label>("object-header-type-label");
            Assert.That(typeName != null, "The Objects header needs a label for the listed type.");
            Assert.That(typeName.text, Is.Empty, "A window with no selected type lists nothing.");
            Assert.That(typeName.style.display.value, Is.EqualTo(DisplayStyle.None), "An unnamed list shows no type label.");
        }

        [UnityTest]
        public IEnumerator ShouldNameTheListedTypeAndElideItsLongDisplayName()
        {
            using TestCleanupScope cleanup = new();
            DataVisualizerWindow window = OpenWindow(cleanup);
            yield return WaitForRefresh(window);
            EditorSurfaceCapture.SettleLayout(window);

            Label typeName = window.rootVisualElement.Q<Label>("object-header-type-label");
            Assert.That(typeName != null, "The Objects header needs a label for the listed type.");

            SelectOnlyType(window, typeof(LongTitledData));
            EditorSurfaceCapture.SettleLayout(window);

            Assert.That(typeName.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "Selecting a type names it in the header.");
            Assert.That(typeName.text, Is.EqualTo(LongTypeTitle), "The header reads the type's display name.");
            Assert.That(
                typeName.tooltip,
                Is.EqualTo(LongTypeTitle + "\n" + typeof(LongTitledData).AssemblyQualifiedName),
                "The hover hint keeps the exact type identity when the name elides."
            );
            Assert.That(typeName.resolvedStyle.textOverflow, Is.EqualTo(TextOverflow.Ellipsis), "A long name elides instead of overflowing.");
            Vector2 needed = typeName.MeasureTextSize(LongTypeTitle, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
            Assert.That(typeName.worldBound.width, Is.LessThan(needed.x), "The long title must be narrower than its full text.");
            Button create = window.rootVisualElement.Q<Button>("create-object-button");
            Assert.That(typeName.worldBound.xMax, Is.LessThanOrEqualTo(create.worldBound.xMin + 0.5f), "The type label stays before the create button.");

            SelectOnlyType(window, typeof(TestDataObject));
            EditorSurfaceCapture.SettleLayout(window);

            Assert.That(typeName.text, Is.EqualTo(nameof(TestDataObject)), "The header follows the current selection.");
            Assert.That(typeName.tooltip, Is.EqualTo(typeof(TestDataObject).AssemblyQualifiedName), "A plain type name hints its assembly-qualified identity.");
            Assert.That(typeName.worldBound.xMax, Is.LessThanOrEqualTo(create.worldBound.xMin + 0.5f), "The updated label stays before the create button.");

            /*
                The header label matters most in a narrow layout, where the catalog row that
                shows the selected type is collapsed and only this pane is on screen.
            */
            window.position = new Rect(40f, 40f, 700f, 700f);
            EditorSurfaceCapture.SettleLayout(window);
            ResponsivePaneLayout panes = ReadPrivateField<ResponsivePaneLayout>(window, "_paneLayout");
            Assert.That(panes.IsCompact, Is.True, "A 700-point window uses the single-pane layout.");
            panes.Show(PaneNavigation.Objects);
            EditorSurfaceCapture.SettleLayout(window);

            Rect objectHeader = window.rootVisualElement.Q("object-header").worldBound;
            Assert.That(objectHeader.width, Is.Positive);
            Assert.That(typeName.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "The compact Objects pane still names its type.");
            Assert.That(typeName.text, Is.EqualTo(nameof(TestDataObject)));
            Assert.That(typeName.worldBound.xMax, Is.LessThanOrEqualTo(objectHeader.xMax + 0.5f), "The compact type label stays inside the header.");

            SelectOnlyType(window, typeof(LongTitledData));
            EditorSurfaceCapture.SettleLayout(window);

            Assert.That(typeName.text, Is.EqualTo(LongTypeTitle), "A compact header reads the long display name.");
            Assert.That(typeName.resolvedStyle.textOverflow, Is.EqualTo(TextOverflow.Ellipsis), "A compact long name elides instead of overflowing.");
            Assert.That(typeName.displayTooltipWhenElided, Is.False, "The elision must not replace the exact identity hint.");
            Vector2 compactNeeded = typeName.MeasureTextSize(LongTypeTitle, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
            Assert.That(typeName.worldBound.width, Is.LessThan(compactNeeded.x), "The compact label must be narrower than its full text.");
            Assert.That(typeName.worldBound.xMax, Is.LessThanOrEqualTo(create.worldBound.xMin + 0.5f), "The compact type label stays before the create button.");
            Assert.That(typeName.worldBound.xMax, Is.LessThanOrEqualTo(objectHeader.xMax + 0.5f), "The compact type label stays inside the header.");
        }

        [CustomDataVisualization(TypeName = LongTypeTitle)]
        private sealed class LongTitledData : ScriptableObject { }
    }
}
