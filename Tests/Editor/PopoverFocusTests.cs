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
    using WallstopStudios.DataVisualizer.Editor.Utilities;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;

    public sealed class PopoverFocusTests
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

        private static DisplayStyle Display(VisualElement element)
        {
            return element.style.display.value;
        }

        private static VisualElement Focused(DataVisualizerWindow window)
        {
            return window.rootVisualElement.focusController.focusedElement as VisualElement;
        }

        private static IEnumerator WaitForFocus(DataVisualizerWindow window, VisualElement expected)
        {
            for (int tick = 0; tick < 120; tick++)
            {
                if (ReferenceEquals(Focused(window), expected))
                {
                    yield break;
                }
                yield return null;
            }
        }

        private static void CreateShownWindow(DataVisualizerWindow window, PerfWindowStateScope state)
        {
            SetPrivateField(window, "_settings", state.Settings);
            SetPrivateField(window, "_userState", state.UserState);
            SetPrivateField(window, "_userStateFilePath", state.FilePath);
            EditorSurfaceCapture.ShowPopup(window);
            SetPrivateField(window, "_settings", state.Settings);
            SetPrivateField(window, "_userState", state.UserState);
            SetPrivateField(window, "_userStateFilePath", state.FilePath);
            window.position = new Rect(40, 40, 1180, 780);
            window.CreateGUI();
            InvokePrivate(window, "LoadInitialContent");
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

        private static void SendKey(VisualElement target, KeyCode key)
        {
            using KeyDownEvent evt = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = key });
            target.SendEvent(evt);
        }

        [UnityTest]
        public IEnumerator ShouldReturnFocusToTheTriggerWhenAPopoverCloses()
        {
            using TestCleanupScope cleanup = new();
            DocsImageCapture.WindowPreferenceSnapshot preferences = DocsImageCapture.WindowPreferenceSnapshot.Capture();
            cleanup.Defer(preferences.Restore);
            preferences.ApplyWidePanes();
            cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
            DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
            string output = "Temp/PopoverFocus-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() => System.IO.Directory.Delete(output, recursive: true));
            using PerfWindowStateScope state = new(settings, new DataVisualizerUserState(), output);
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            CreateShownWindow(window, state);
            yield return null;

            Button trigger = ReadPrivateField<Button>(window, "_settingsButton");
            VisualElement popover = ReadPrivateField<VisualElement>(window, "_settingsPopover");
            trigger.Focus();
            yield return null;
            Assert.That(Focused(window), Is.SameAs(trigger), "The trigger starts focused.");

            InvokePrivate(window, "TogglePopover", popover, trigger);
            yield return WaitForFocus(window, popover);
            Assert.That(Display(popover), Is.EqualTo(DisplayStyle.Flex));
            Assert.That(Focused(window), Is.SameAs(popover), "An open popover owns focus.");

            SendKey(popover, KeyCode.Escape);
            yield return WaitForFocus(window, trigger);
            Assert.That(Display(popover), Is.EqualTo(DisplayStyle.None));
            Assert.That(Focused(window), Is.SameAs(trigger), "Closing a popover returns focus to the control that opened it.");
        }

        [UnityTest]
        public IEnumerator ShouldKeepFocusInTheObjectListAfterAConfirmedDelete()
        {
            using TestCleanupScope cleanup = new();
            DocsImageCapture.WindowPreferenceSnapshot preferences = DocsImageCapture.WindowPreferenceSnapshot.Capture();
            cleanup.Defer(preferences.Restore);
            preferences.ApplyWidePanes();
            cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
            DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
            string output = "Temp/PopoverDeleteFocus-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() => System.IO.Directory.Delete(output, recursive: true));
            using PerfWindowStateScope state = new(settings, new DataVisualizerUserState(), output);
            Type type = typeof(TestDataObject);
            state.UserState.managedTypeNames.Add(type.FullName);
            state.Settings.HydrateFrom(state.UserState);
            string folder = "Assets/PopoverDeleteFocus_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            string[] paths = { folder + "/First.asset", folder + "/Second.asset", folder + "/Third.asset" };
            foreach (string path in paths)
            {
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<TestDataObject>(), path);
            }
            AssetDatabase.SaveAssets();
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            CreateShownWindow(window, state);
            ReadPrivateField<NamespaceController>(window, "_namespaceController").SelectType(window, type);
            yield return WaitForRefresh(window);
            EditorSurfaceCapture.SettleLayout(window);

            ListView list = ReadPrivateField<ListView>(window, "_objectListView");
            List<ScriptableObject> rows = ReadPrivateField<List<ScriptableObject>>(window, "_filteredObjects");
            Assert.That(rows, Has.Count.EqualTo(paths.Length));
            ScriptableObject doomed = rows[1];
            InvokePrivate(window, "SetActiveObject", doomed, false);
            yield return null;
            EditorSurfaceCapture.SettleLayout(window);
            VisualElement row = list.Query<VisualElement>().Where(element => ReferenceEquals(element.userData, doomed)).First();
            Button actions = row.Q<Button>("object-actions-button");
            Assert.That(actions != null);
            actions.Focus();
            yield return null;
            Assert.That(Focused(window), Is.SameAs(actions));

            SendKey(actions, KeyCode.Delete);
            VisualElement confirm = ReadPrivateField<VisualElement>(window, "_confirmDeletePopover");
            for (int tick = 0; tick < 120 && Display(confirm) != DisplayStyle.Flex; tick++)
            {
                yield return null;
            }
            Assert.That(Display(confirm), Is.EqualTo(DisplayStyle.Flex), "Delete opens the confirmation popover.");
            SendKey(confirm, KeyCode.Return);
            yield return WaitForFocus(window, list);
            Assert.That(AssetDatabase.LoadAssetAtPath<ScriptableObject>(paths[1]) == null, "The confirmed delete removes the asset.");
            Assert.That(ReadPrivateField<List<ScriptableObject>>(window, "_filteredObjects"), Has.Count.EqualTo(paths.Length - 1));
            Assert.That(Focused(window), Is.SameAs(list), "Deleting the focused row keeps keyboard focus in the object list.");
        }
    }
}
