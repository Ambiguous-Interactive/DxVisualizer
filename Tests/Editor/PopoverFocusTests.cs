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
    using WallstopStudios.DataVisualizer.Editor.UI;
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

        private static void SetDataFolderPath(DataVisualizerSettings settings, string dataFolderPath)
        {
            FieldInfo field = typeof(DataVisualizerSettings).GetField("_dataFolderPath", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field != null, "The settings asset holds a data folder path.");
            field.SetValue(settings, dataFolderPath);
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

        private static bool FocusIsWithin(DataVisualizerWindow window, VisualElement container)
        {
            VisualElement focused = Focused(window);
            return focused != null && (focused == container || container.Contains(focused));
        }

        private static IEnumerator WaitForFocusWithin(DataVisualizerWindow window, VisualElement container)
        {
            for (int tick = 0; tick < 120; tick++)
            {
                if (FocusIsWithin(window, container))
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
            Assert.That(Focused(window), Is.SameAs(confirm), "The delete dialog owns focus before it handles the confirm key.");
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

        [UnityTest]
        public IEnumerator ShouldFocusTheRenameNameFieldWhenTheRenamePopoverOpens()
        {
            using TestCleanupScope cleanup = new();
            Type type = typeof(PopoverNameData);
            DataVisualizerWindow window = OpenSettingsWindow(
                cleanup,
                "PopoverRenameFocus",
                scope =>
                {
                    scope.UserState.managedTypeNames.Add(type.FullName);
                    scope.Settings.HydrateFrom(scope.UserState);
                }
            );
            string folder = "Assets/PopoverRenameFocus_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            string path = folder + "/Renamed.asset";
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<PopoverNameData>(), path);
            AssetDatabase.SaveAssets();
            ReadPrivateField<NamespaceController>(window, "_namespaceController").SelectType(window, type);
            ScriptableObject target = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            Assert.That(target != null, "The fixture asset loads.");
            InvokePrivate(window, "SetActiveObject", target, false);
            yield return null;

            ListView list = ReadPrivateField<ListView>(window, "_objectListView");
            list.Focus();
            yield return null;

            SendKey(list, KeyCode.F2);
            VisualElement rename = ReadPrivateField<VisualElement>(window, "_renamePopover");
            for (int tick = 0; tick < 120 && Display(rename) != DisplayStyle.Flex; tick++)
            {
                yield return null;
            }
            Assert.That(Display(rename), Is.EqualTo(DisplayStyle.Flex), "Rename opens the popover.");
            TextField nameField = rename.Q<TextField>(name: "rename-textfield");
            Assert.That(nameField != null, "The rename popover holds a name field.");
            /*
                The field takes focus so the keystrokes that follow the shortcut edit
                the name. While the popover container holds focus, typed characters
                never reach the field.
            */
            yield return WaitForFocusWithin(window, nameField);
            Assert.That(FocusIsWithin(window, nameField), "The rename name field takes typing focus.");
        }

        [UnityTest]
        public IEnumerator ShouldFocusTheCreateNameFieldWhenTheCreatePopoverOpens()
        {
            using TestCleanupScope cleanup = new();
            Type type = typeof(PopoverNameData);
            DataVisualizerWindow window = OpenSettingsWindow(
                cleanup,
                "PopoverCreateFocus",
                scope =>
                {
                    scope.UserState.managedTypeNames.Add(type.FullName);
                    scope.Settings.HydrateFrom(scope.UserState);
                }
            );
            ReadPrivateField<NamespaceController>(window, "_namespaceController").SelectType(window, type);
            yield return null;

            Button create = ReadPrivateField<Button>(window, "_createObjectButton");
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = create;
                create.SendEvent(submit);
            }
            VisualElement popover = ReadPrivateField<VisualElement>(window, "_createPopover");
            for (int tick = 0; tick < 120 && Display(popover) != DisplayStyle.Flex; tick++)
            {
                yield return null;
            }
            Assert.That(Display(popover), Is.EqualTo(DisplayStyle.Flex), "The create button opens the create popover.");
            TextField nameField = popover.Q<TextField>(name: "create-textfield");
            Assert.That(nameField != null, "The create popover holds a name field.");
            yield return WaitForFocusWithin(window, nameField);
            Assert.That(FocusIsWithin(window, nameField), "The create name field takes typing focus.");
        }

        [UnityTest]
        public IEnumerator ShouldFocusTheTypeSearchFieldWhenTheTypePopoverOpens()
        {
            using TestCleanupScope cleanup = new();
            DataVisualizerWindow window = OpenSettingsWindow(cleanup, "PopoverTypeSearchFocus");
            yield return null;

            window.OpenTypeSearchPopover();
            VisualElement popover = ReadPrivateField<VisualElement>(window, "_typeAddPopover");
            for (int tick = 0; tick < 120 && Display(popover) != DisplayStyle.Flex; tick++)
            {
                yield return null;
            }
            Assert.That(Display(popover), Is.EqualTo(DisplayStyle.Flex), "The type finder opens.");
            TextField searchField = ReadPrivateField<TextField>(window, "_typeAddSearchField");
            yield return WaitForFocusWithin(window, searchField);
            Assert.That(FocusIsWithin(window, searchField), "The type finder search field takes typing focus.");
        }

        [UnityTest]
        public IEnumerator ShouldRevealTheDataFolderWhenThePathDisplayTakesTheConfirmKey()
        {
            using TestCleanupScope cleanup = new();
            string folder = "Assets/PopoverDataFolderReveal_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            /*
                A configured folder either exists or does not. Each case proves that a
                configured key reaches the reveal command: only a missing folder reports a
                warning that names its path, so a pointer-only control fails those cases.
            */
            /*
                The last two cases remap Select to Up Arrow, which also navigates the open
                popover: only a focused path that owns its selection keys still reveals.
            */
            string missingFolder = folder + "/Missing";
            (string DataFolderPath, KeyCode Key, KeyCode? SelectKey, bool FolderExists, bool WarnsAboutThePath)[] cases =
            {
                (folder, KeyCode.Return, null, true, false),
                (missingFolder, KeyCode.Return, null, false, true),
                (missingFolder, KeyCode.Space, null, false, true),
                (missingFolder, KeyCode.UpArrow, null, false, false),
                (missingFolder, KeyCode.UpArrow, KeyCode.UpArrow, false, true),
            };
            foreach ((string dataFolderPath, KeyCode key, KeyCode? selectKey, bool folderExists, bool warnsAboutThePath) in cases)
            {
                using TestCleanupScope caseCleanup = new();
                DataVisualizerWindow window = OpenSettingsWindow(
                    caseCleanup,
                    "PopoverDataFolderReveal",
                    scope =>
                    {
                        SetDataFolderPath(scope.Settings, dataFolderPath);
                        if (!selectKey.HasValue)
                        {
                            return;
                        }

                        DataVisualizerInputProfile profile = ScriptableObject.CreateInstance<DataVisualizerInputProfile>();
                        caseCleanup.Defer(() => UnityEngine.Object.DestroyImmediate(profile));
                        profile.Bindings.Navigation.SelectKey = selectKey.Value;
                        scope.Settings.inputProfile = profile;
                    }
                );
                yield return null;

                VisualElement popover = ReadPrivateField<VisualElement>(window, "_settingsPopover");
                InvokePrivate(window, "TogglePopover", popover, ReadPrivateField<Button>(window, "_settingsButton"));
                yield return WaitForFocus(window, popover);
                EditorSurfaceCapture.SettleLayout(window);
                Label display = popover.Q<Label>("data-folder-display");
                Assert.That(display != null, "The settings popover shows the data folder path.");
                Assert.That(display.focusable, "The data folder path takes keyboard focus.");
                display.Focus();
                yield return null;
                Assert.That(Focused(window), Is.SameAs(display), "The data folder path holds focus.");

                using ConsoleCapture capture = new();
                SendKey(display, key);
                yield return null;
                Assert.That(
                    capture.Contains(dataFolderPath),
                    Is.EqualTo(warnsAboutThePath),
                    warnsAboutThePath ? "A missing data folder reports its configured path." : "An existing data folder reveals without a warning."
                );

                /*
                    Project-window highlighting has no readable result, so the reveal reports
                    the folder it resolves. An existing folder resolves to that folder object.
                */
                object[] resolved = { null };
                bool isResolved = (bool)InvokePrivate(window, "TryResolveDataFolder", resolved);
                Assert.That(isResolved, Is.EqualTo(folderExists), "Only an existing data folder resolves for a reveal.");
                UnityEngine.Object expectedFolder = folderExists ? AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(dataFolderPath) : null;
                Assert.That(resolved[0] as UnityEngine.Object == expectedFolder, "The reveal resolves the configured folder.");
            }
        }
    }
}
