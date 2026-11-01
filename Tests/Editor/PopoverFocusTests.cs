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

        private static DataVisualizerWindow OpenSettingsWindow(TestCleanupScope cleanup, string outputName, Action<PerfWindowStateScope> configure = null)
        {
            DocsImageCapture.WindowPreferenceSnapshot preferences = DocsImageCapture.WindowPreferenceSnapshot.Capture();
            cleanup.Defer(preferences.Restore);
            preferences.ApplyWidePanes();
            cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
            DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
            string output = "Temp/" + outputName + "-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() => System.IO.Directory.Delete(output, recursive: true));
            PerfWindowStateScope state = new(settings, new DataVisualizerUserState(), output);
            cleanup.Defer(() => state.Dispose());
            configure?.Invoke(state);
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            CreateShownWindow(window, state);
            return window;
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

        private static void SendConfirmKey(VisualElement target)
        {
            using KeyDownEvent evt = KeyDownEvent.GetPooled('\n', KeyCode.Return, EventModifiers.None);
            target.SendEvent(evt);
        }

        [UnityTest]
        public IEnumerator ShouldReturnFocusToTheTriggerWhenAPopoverCloses()
        {
            using TestCleanupScope cleanup = new();
            DataVisualizerWindow window = OpenSettingsWindow(cleanup, "PopoverFocus");
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
        public IEnumerator ShouldReturnFocusToTheTriggerWhenAnOutsideClickClearsFocus()
        {
            using TestCleanupScope cleanup = new();
            DataVisualizerWindow window = OpenSettingsWindow(cleanup, "PopoverOutsideClick");
            yield return null;

            Button trigger = ReadPrivateField<Button>(window, "_settingsButton");
            VisualElement popover = ReadPrivateField<VisualElement>(window, "_settingsPopover");
            trigger.Focus();
            yield return null;
            InvokePrivate(window, "TogglePopover", popover, trigger);
            yield return WaitForFocus(window, popover);
            Assert.That(Focused(window), Is.SameAs(popover), "An open popover owns focus.");

            // A pointer press on a plain element clears panel focus, and the press then closes the popover.
            popover.Blur();
            Assert.That(Focused(window) == null, "A plain outside element leaves no focused control.");
            using (PointerDownEvent outside = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0 }))
            {
                outside.target = window.rootVisualElement;
                window.rootVisualElement.SendEvent(outside);
            }

            yield return WaitForFocus(window, trigger);
            Assert.That(Display(popover), Is.EqualTo(DisplayStyle.None));
            Assert.That(Focused(window), Is.SameAs(trigger), "An outside click that clears focus returns it to the trigger.");
        }

        [UnityTest]
        public IEnumerator ShouldKeepNewPanelFocusWhenAClosingPopoverRestoresFocus()
        {
            using TestCleanupScope cleanup = new();
            DataVisualizerWindow window = OpenSettingsWindow(cleanup, "PopoverFocusHandoff");
            yield return null;

            Button trigger = ReadPrivateField<Button>(window, "_settingsButton");
            VisualElement popover = ReadPrivateField<VisualElement>(window, "_settingsPopover");
            trigger.Focus();
            yield return null;
            InvokePrivate(window, "TogglePopover", popover, trigger);
            yield return WaitForFocus(window, popover);
            Assert.That(Focused(window), Is.SameAs(popover), "An open popover owns focus.");

            /*
                Opening a tool closes the popover and focuses the tool in the same call. The
                popover's deferred focus restore must not take focus back from that tool.
            */
            window.OpenAssetValidation();
            AssetValidationPanel panel = window.rootVisualElement.Q<AssetValidationPanel>();
            Assert.That(panel != null, "Opening validation adds its panel.");
            for (int tick = 0; tick < 3; tick++)
            {
                yield return null;
            }

            Assert.That(Display(popover), Is.EqualTo(DisplayStyle.None), "The popover closes.");
            Assert.That(Focused(window), Is.SameAs(panel), "The tool that replaced the popover keeps focus.");
        }

        [UnityTest]
        public IEnumerator ShouldFocusTheCreateButtonAfterDeletingTheLastObject()
        {
            using TestCleanupScope cleanup = new();
            Type type = typeof(LastObjectDeleteData);
            DataVisualizerWindow window = OpenSettingsWindow(
                cleanup,
                "PopoverDeleteLastFocus",
                scope =>
                {
                    scope.UserState.managedTypeNames.Add(type.FullName);
                    scope.Settings.HydrateFrom(scope.UserState);
                }
            );
            string folder = "Assets/PopoverDeleteLastFocus_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            string path = folder + "/Only.asset";
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<LastObjectDeleteData>(), path);
            AssetDatabase.SaveAssets();
            ReadPrivateField<NamespaceController>(window, "_namespaceController").SelectType(window, type);
            yield return WaitForRefresh(window);
            EditorSurfaceCapture.SettleLayout(window);

            ListView list = ReadPrivateField<ListView>(window, "_objectListView");
            Button create = ReadPrivateField<Button>(window, "_createObjectButton");
            List<ScriptableObject> rows = ReadPrivateField<List<ScriptableObject>>(window, "_filteredObjects");
            ScriptableObject doomed = rows.Find(candidate => AssetDatabase.GetAssetPath(candidate) == path);
            Assert.That(doomed != null, "The type filter lists the created asset.");
            InvokePrivate(window, "SetActiveObject", doomed, false);
            yield return WaitForRefresh(window);
            EditorSurfaceCapture.SettleLayout(window);
            list.Focus();
            yield return null;

            SendKey(list, KeyCode.Delete);
            VisualElement confirm = ReadPrivateField<VisualElement>(window, "_confirmDeletePopover");
            for (int tick = 0; tick < 120 && Display(confirm) != DisplayStyle.Flex; tick++)
            {
                yield return null;
            }
            Assert.That(Display(confirm), Is.EqualTo(DisplayStyle.Flex), "Delete opens the confirmation popover.");
            /*
                Key events reach the focused element, and the popover takes focus on a
                scheduled tick after it becomes visible. Sending the confirm key earlier
                delivers it to the object list instead of the dialog.
            */
            yield return WaitForFocus(window, confirm);
            SendConfirmKey(confirm.Q(className: "popover-drag-handle"));
            yield return WaitForFocus(window, create);
            Assert.That(AssetDatabase.LoadAssetAtPath<ScriptableObject>(path) == null, "The confirmed delete removes the last asset.");
            Assert.That(Display(list), Is.EqualTo(DisplayStyle.None), "The empty list is hidden.");
            Assert.That(Focused(window), Is.SameAs(create), "An empty list hands focus to the create button.");
        }

        [UnityTest]
        public IEnumerator ShouldKeepFocusInTheObjectListAfterAConfirmedDelete()
        {
            using TestCleanupScope cleanup = new();
            Type type = typeof(TestDataObject);
            DataVisualizerWindow window = OpenSettingsWindow(
                cleanup,
                "PopoverDeleteFocus",
                scope =>
                {
                    scope.UserState.managedTypeNames.Add(type.FullName);
                    scope.Settings.HydrateFrom(scope.UserState);
                }
            );
            string folder = "Assets/PopoverDeleteFocus_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            string[] paths = { folder + "/First.asset", folder + "/Second.asset", folder + "/Third.asset" };
            foreach (string path in paths)
            {
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<TestDataObject>(), path);
            }
            AssetDatabase.SaveAssets();
            ReadPrivateField<NamespaceController>(window, "_namespaceController").SelectType(window, type);
            yield return WaitForRefresh(window);
            EditorSurfaceCapture.SettleLayout(window);

            ListView list = ReadPrivateField<ListView>(window, "_objectListView");
            List<ScriptableObject> rows = ReadPrivateField<List<ScriptableObject>>(window, "_filteredObjects");
            ScriptableObject doomed = rows.Find(candidate => AssetDatabase.GetAssetPath(candidate) == paths[1]);
            Assert.That(doomed != null, "The type filter lists the created asset.");
            InvokePrivate(window, "SetActiveObject", doomed, false);
            yield return WaitForRefresh(window);
            EditorSurfaceCapture.SettleLayout(window);
            list.Focus();
            yield return null;

            SendKey(list, KeyCode.Delete);
            VisualElement confirm = ReadPrivateField<VisualElement>(window, "_confirmDeletePopover");
            for (int tick = 0; tick < 120 && Display(confirm) != DisplayStyle.Flex; tick++)
            {
                yield return null;
            }
            Assert.That(Display(confirm), Is.EqualTo(DisplayStyle.Flex), "Delete opens the confirmation popover.");
            for (int tick = 0; tick < 30 && Focused(window) != confirm; tick++)
            {
                yield return null;
            }
            // The popover key handler hangs off the header, so the key press starts there.
            SendConfirmKey(confirm.Q(className: "popover-drag-handle"));
            yield return WaitForFocus(window, list);
            Assert.That(Display(confirm), Is.EqualTo(DisplayStyle.None), "Confirming closes the delete popover.");
            Assert.That(AssetDatabase.LoadAssetAtPath<ScriptableObject>(paths[1]) == null, "The confirmed delete removes the asset.");
            List<ScriptableObject> remaining = ReadPrivateField<List<ScriptableObject>>(window, "_filteredObjects");
            Assert.That(remaining.Exists(candidate => AssetDatabase.GetAssetPath(candidate) == paths[1]), Is.False, "The deleted asset leaves the list.");
            Assert.That(remaining.Exists(candidate => AssetDatabase.GetAssetPath(candidate) == paths[0]), Is.True, "Sibling assets stay listed.");
            Assert.That(Focused(window), Is.SameAs(list), "Deleting the focused row keeps keyboard focus in the object list.");
        }
    }
}
