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
    using WallstopStudios.DataVisualizer.Editor.Styles;
    using WallstopStudios.DataVisualizer.Editor.UI;
    using WallstopStudios.DataVisualizer.Editor.Utilities;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;

    public sealed class PlayModeMutationGateTests
    {
        private static FieldInfo _instanceField;

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

        private static string CreateFixtureFolder()
        {
            string folder = "Assets/DataVisualizerMutationGateTests_" + Guid.NewGuid().ToString("N");
            EnsureFolderExists(folder);
            return folder;
        }

        private static ScriptableObject CreateFixtureAsset(string folder, string assetName)
        {
            string assetPath = folder + "/" + assetName;
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<TestDataObject>(), assetPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
        }

        private static object ReadSharedInstance()
        {
            if (_instanceField == null)
            {
                _instanceField = typeof(DataVisualizerWindow).GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
            }

            return _instanceField?.GetValue(null);
        }

        private static void RestoreSharedInstance(object previousInstance)
        {
            _instanceField?.SetValue(null, previousInstance);
        }

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

        private static T ReadControllerField<T>(object controller, string fieldName)
        {
            FieldInfo field = controller.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(field != null, $"The {fieldName} field must exist.");
            return (T)field.GetValue(controller);
        }

        private static void SuspendForPlayMode(DataVisualizerWindow window)
        {
            window.HandlePlayModeStateChanged(PlayModeStateChange.ExitingEditMode);
        }

        private static void ResumeFromPlayMode(DataVisualizerWindow window)
        {
            window.HandlePlayModeStateChanged(PlayModeStateChange.EnteredEditMode);
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
            Assert.Fail("Asset changes did not settle after the Play transition.");
        }

        private static void AssertRowOrderingControls(VisualElement row, bool canMoveUp, bool canMoveDown)
        {
            Assert.That(row.Q<Button>("go-up-button").ClassListContains("go-button-disabled"), Is.EqualTo(!canMoveUp));
            Assert.That(row.Q<Button>("go-down-button").ClassListContains("go-button-disabled"), Is.EqualTo(!canMoveDown));
        }

        private static void SendLabelToggleKey(VisualElement toggle, KeyCode key)
        {
            using KeyDownEvent evt = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = key });
            evt.target = toggle;
            toggle.SendEvent(evt);
        }

        [Test]
        public void ShouldMatchRefreshTypesByExactIdentityInTheirCanonicalNamespace()
        {
            using TestCleanupScope cleanup = new();
            object previousInstance = ReadSharedInstance();
            cleanup.Defer(() => RestoreSharedInstance(previousInstance));
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));
            Dictionary<string, List<Type>> managed = ReadPrivateField<Dictionary<string, List<Type>>>(window, "_scriptableObjectTypes");
            managed.Clear();
            Type[][] datasets = CatalogLayoutScaleTests.CreateTypes();
            foreach (Type type in datasets[1])
            {
                string key = NamespaceController.GetNamespaceKey(type);
                if (!managed.TryGetValue(key, out List<Type> types))
                {
                    types = new List<Type>();
                    managed.Add(key, types);
                }
                types.Add(type);
            }
            Type last = datasets[1][datasets[1].Length - 1];
            Assert.That((bool)InvokePrivate(window, "IsTrackedAssetType", last));
            Assert.That((bool)InvokePrivate(window, "IsTrackedAssetType", typeof(string)), Is.False);
            Assert.That((bool)InvokePrivate(window, "IsTrackedAssetType", typeof(DataVisualizerSettings)), "Settings imports still request a full refresh.");
            Type collisionA = typeof(TypeIdentityCollision.First.Data.OrderCollisionData);
            Type collisionB = typeof(TypeIdentityCollision.Second.Data.OrderCollisionData);
            managed.Add(NamespaceController.GetNamespaceKey(collisionA), new List<Type> { collisionA });
            Assert.That((bool)InvokePrivate(window, "IsTrackedAssetType", collisionA));
            Assert.That((bool)InvokePrivate(window, "IsTrackedAssetType", collisionB), Is.False, "Equal short names do not match.");
            managed[NamespaceController.GetNamespaceKey(last)].Remove(last);
            Assert.That((bool)InvokePrivate(window, "IsTrackedAssetType", last), Is.False, "Catalog removals take effect without a cache rebuild.");
            managed[NamespaceController.GetNamespaceKey(last)].Add(last);
            Assert.That((bool)InvokePrivate(window, "IsTrackedAssetType", last), "Catalog additions take effect without a cache rebuild.");
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ShouldOrderSelectedAssetsByShortcutWithoutRevealingFilteredRows(bool projectState, bool commandKey)
        {
            using TestCleanupScope cleanup = new();
            string folder = CreateFixtureFolder();
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            object previousInstance = ReadSharedInstance();
            cleanup.Defer(() => RestoreSharedInstance(previousInstance));
            DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
            string output = "Temp/OrderShortcut-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() => System.IO.Directory.Delete(output, recursive: true));
            using PerfWindowStateScope state = new(settings, new DataVisualizerUserState(), output);
            state.Settings.persistStateInSettingsAsset = projectState;
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            using TestCleanupScope windowCleanup = new();
            windowCleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));
            SetPrivateField(window, "_settings", state.Settings);
            SetPrivateField(window, "_userState", state.UserState);
            SetPrivateField(window, "_userStateFilePath", state.FilePath);
            ScriptableObject first = CreateFixtureAsset(folder, "First.asset");
            ScriptableObject selected = CreateFixtureAsset(folder, "Selected.asset");
            ScriptableObject last = CreateFixtureAsset(folder, "Last.asset");
            List<ScriptableObject> all = ReadPrivateField<List<ScriptableObject>>(window, "_selectedObjects");
            List<ScriptableObject> visible = ReadPrivateField<List<ScriptableObject>>(window, "_filteredObjects");
            SetPrivateField(window, "_objectListView", null);
            all.Clear();
            all.AddRange(new[] { first, selected, last });
            visible.Clear();
            visible.AddRange(new[] { first, last });
            SetPrivateField(window, "_selectedObject", selected);
            EventModifiers modifier = commandKey ? EventModifiers.Command : EventModifiers.Control;
            foreach (KeyCode key in new[] { KeyCode.Home, KeyCode.End })
            {
                using KeyDownEvent evt = KeyDownEvent.GetPooled(
                    new Event
                    {
                        type = EventType.KeyDown,
                        keyCode = key,
                        modifiers = modifier,
                    }
                );
                Assert.That((bool)InvokePrivate(window, "HandleObjectActionShortcut", evt));
                Assert.That(all[key == KeyCode.Home ? 0 : all.Count - 1], Is.SameAs(selected));
                Assert.That(visible.Contains(selected), Is.False, "Ordering must not reveal a label-filtered asset.");
                List<string> saved = (List<string>)InvokePrivate(window, "GetObjectOrderForType", typeof(TestDataObject));
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(selected));
                Assert.That(saved[key == KeyCode.Home ? 0 : saved.Count - 1], Is.EqualTo(guid));
                Assert.That(ReadPrivateField<ScriptableObject>(window, "_selectedObject"), Is.SameAs(selected));
            }
            DataVisualizerInputProfile profile = ScriptableObject.CreateInstance<DataVisualizerInputProfile>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(profile));
            state.Settings.inputProfile = profile;
            profile.Bindings.moveFirst = new EditorInputBinding(KeyCode.F7, EventModifiers.Alt);
            profile.Bindings.moveLast = new EditorInputBinding(KeyCode.F8, EventModifiers.Shift);
            using (
                KeyDownEvent oldKey = KeyDownEvent.GetPooled(
                    new Event
                    {
                        type = EventType.KeyDown,
                        keyCode = KeyCode.Home,
                        modifiers = modifier,
                    }
                )
            )
            {
                Assert.That((bool)InvokePrivate(window, "HandleObjectActionShortcut", oldKey), Is.False, "Remapped actions no longer accept the old gesture.");
            }
            using (
                KeyDownEvent remapped = KeyDownEvent.GetPooled(
                    new Event
                    {
                        type = EventType.KeyDown,
                        keyCode = KeyCode.F7,
                        modifiers = EventModifiers.Alt,
                    }
                )
            )
            {
                Assert.That((bool)InvokePrivate(window, "HandleObjectActionShortcut", remapped));
                Assert.That(all[0], Is.SameAs(selected));
            }
            using (
                KeyDownEvent remapped = KeyDownEvent.GetPooled(
                    new Event
                    {
                        type = EventType.KeyDown,
                        keyCode = KeyCode.F8,
                        modifiers = EventModifiers.Shift,
                    }
                )
            )
            {
                Assert.That((bool)InvokePrivate(window, "HandleObjectActionShortcut", remapped));
                Assert.That(all[all.Count - 1], Is.SameAs(selected));
            }
            state.Settings.inputProfile = null;
            all.Remove(selected);
            ScriptableObject[] members = all.ToArray();
            string[] savedBeforeStaleCallback = ((List<string>)InvokePrivate(window, "GetObjectOrderForType", typeof(TestDataObject))).ToArray();
            SetPrivateField(window, "_isLoadingObjectsAsync", true);
            SetPrivateField(window, "_asyncLoadTargetType", typeof(TestDataObject));
            Dictionary<string, int> canonical = ReadPrivateField<Dictionary<string, int>>(window, "_asyncDisplayOrderByGuid");
            canonical.Clear();
            canonical["pending-guid"] = 0;
            foreach (string method in new[] { "MoveObjectToTop", "MoveObjectToBottom" })
            {
                InvokePrivate(window, method, selected);
                Assert.That(all, Is.EqualTo(members), "Stale actions cannot add objects to the active type.");
                Assert.That(visible.Contains(selected), Is.False);
                Assert.That(canonical.Count, Is.EqualTo(1), "Stale actions cannot mutate pending canonical order.");
                Assert.That(canonical["pending-guid"], Is.Zero);
                Assert.That((List<string>)InvokePrivate(window, "GetObjectOrderForType", typeof(TestDataObject)), Is.EqualTo(savedBeforeStaleCallback));
            }
            InvokePrivate(window, "CloneObject", selected);
            Assert.That(AssetDatabase.FindAssets("", new[] { folder }).Length, Is.EqualTo(3), "Stale clone actions must not add an asset or pollute active rows.");
            Assert.That(all, Is.EqualTo(members));
            SetPrivateField(window, "_isLoadingObjectsAsync", false);
            all.Add(selected);
            SetPrivateField(window, "_suspendedForPlayMode", true);
            using KeyDownEvent blocked = KeyDownEvent.GetPooled(
                new Event
                {
                    type = EventType.KeyDown,
                    keyCode = KeyCode.Home,
                    modifiers = modifier,
                }
            );
            Assert.That((bool)InvokePrivate(window, "HandleObjectActionShortcut", blocked), Is.False);
            InvokePrivate(window, "MoveObjectToTop", selected);
            Assert.That(all[all.Count - 1], Is.SameAs(selected), "Direct menu callbacks must also respect suspension.");
        }

        [UnityTest]
        public IEnumerator ShouldChooseProcessorScopeWithOwnerInput()
        {
            using TestCleanupScope preferencesCleanup = new();
            DocsImageCapture.WindowPreferenceSnapshot preferences = DocsImageCapture.WindowPreferenceSnapshot.Capture();
            preferencesCleanup.Defer(preferences.Restore);
            foreach (bool projectState in new[] { false, true })
            {
                preferences.ApplyWidePanes();
                using TestCleanupScope cleanup = new();
                object previousInstance = ReadSharedInstance();
                cleanup.Defer(() => RestoreSharedInstance(previousInstance));
                cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
                DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
                string output = "Temp/ProcessorInput-" + Guid.NewGuid().ToString("N");
                cleanup.Defer(() => System.IO.Directory.Delete(output, recursive: true));
                using PerfWindowStateScope state = new(settings, new DataVisualizerUserState(), output);
                state.Settings.persistStateInSettingsAsset = projectState;
                Type type = typeof(TestDataObject);
                state.UserState.managedTypeNames.Add(type.FullName);
                state.Settings.HydrateFrom(state.UserState);
                string folder = CreateFixtureFolder();
                cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
                CreateFixtureAsset(folder, "Input.asset");
                DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
                using TestCleanupScope windowCleanup = new();
                windowCleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
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
                ReadPrivateField<List<WallstopStudios.DataVisualizer.IDataProcessor>>(window, "_allDataProcessors")
                    .Add(new EditorProcessorAutomationTests.JsonProbeProcessor<int>());
                ReadPrivateField<NamespaceController>(window, "_namespaceController").SelectType(window, type);
                yield return WaitForRefresh(window);
                ProcessorState config = (ProcessorState)InvokePrivate(window, "LoadOrCreateProcessorState", type);
                Button collapse = window.rootVisualElement.Q<Button>("processor-collapse-toggle");
                Assert.That(collapse != null && collapse.focusable, "Processor expand/collapse must be reachable before its scope choices.");
                Assert.That(config.isCollapsed);
                collapse.Focus();
                SendLabelToggleKey(collapse, KeyCode.Return);
                Assert.That(config.isCollapsed, Is.False);
                yield return null;
                HorizontalToggle scope = window.rootVisualElement.Q<HorizontalToggle>("processor-logic-toggle");
                Assert.That(scope != null);
                Assert.That(scope.LeftLabel.focusable && scope.RightLabel.focusable, "Both processor scope choices must enter the focus ring.");
                EditorSurfaceCapture.SettleLayout(window);
                Assert.That(scope.LeftLabel.worldBound.Overlaps(window.rootVisualElement.worldBound));
                Assert.That(config.logic, Is.EqualTo(ProcessorLogic.Filtered));
                scope.LeftLabel.Focus();
                SendLabelToggleKey(scope.LeftLabel, KeyCode.Return);
                yield return new WaitForSecondsRealtime(0.2f);
                Assert.That(config.logic, Is.EqualTo(ProcessorLogic.All));
                scope.LeftLabel.Focus();
                Assert.That(window.rootVisualElement.focusController.focusedElement, Is.SameAs(scope.LeftLabel), "Focus stays on the activated ALL choice.");
                using (KeyDownEvent next = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = window.Input.nextFocus.key }))
                {
                    scope.LeftLabel.SendEvent(next);
                }
                Assert.That(window.rootVisualElement.focusController.focusedElement, Is.SameAs(scope.RightLabel));
                SendLabelToggleKey(scope.RightLabel, KeyCode.Space);
                yield return new WaitForSecondsRealtime(0.2f);
                Assert.That(config.logic, Is.EqualTo(ProcessorLogic.Filtered));
                DataVisualizerInputProfile profile = ScriptableObject.CreateInstance<DataVisualizerInputProfile>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(profile));
                profile.Bindings.Navigation.SelectKey = KeyCode.F6;
                profile.Bindings.Navigation.AlternateSelectKey = KeyCode.None;
                profile.Bindings.Navigation.ToggleKey = KeyCode.None;
                state.Settings.inputProfile = profile;
                Assert.That(ReadPrivateField<ScriptableObject>(window, "_selectedObject") != null, "A loaded asset must make the competing Rename shortcut available.");
                profile.Bindings.Navigation.SelectKey = KeyCode.F2;
                scope.LeftLabel.Focus();
                SendLabelToggleKey(scope.LeftLabel, KeyCode.F2);
                Assert.That(config.logic, Is.EqualTo(ProcessorLogic.All), "Configured choice selection takes precedence over the global Rename shortcut.");
                Assert.That(ReadPrivateField<VisualElement>(window, "_activePopover") == null);
                scope.RightLabel.Focus();
                SendLabelToggleKey(scope.RightLabel, KeyCode.F2);
                Assert.That(config.logic, Is.EqualTo(ProcessorLogic.Filtered));
                foreach (EventModifiers locks in new[] { EventModifiers.CapsLock, EventModifiers.Numeric, EventModifiers.CapsLock | EventModifiers.Numeric })
                {
                    foreach (Label choice in new[] { scope.LeftLabel, scope.RightLabel })
                    {
                        choice.Focus();
                        using KeyDownEvent locked = KeyDownEvent.GetPooled(
                            new Event
                            {
                                type = EventType.KeyDown,
                                keyCode = KeyCode.F2,
                                modifiers = locks,
                            }
                        );
                        locked.target = choice;
                        choice.SendEvent(locked);
                        Assert.That(
                            config.logic,
                            Is.EqualTo(choice == scope.LeftLabel ? ProcessorLogic.All : ProcessorLogic.Filtered),
                            "Lock flags must not block selection or run Rename."
                        );
                        Assert.That(ReadPrivateField<VisualElement>(window, "_activePopover") == null);
                    }
                }
                profile.Bindings.Navigation.SelectKey = KeyCode.F6;
                foreach (KeyCode key in new[] { KeyCode.Return, KeyCode.Space, KeyCode.KeypadEnter, KeyCode.F6 })
                {
                    scope.LeftLabel.Focus();
                    using (KeyDownEvent activate = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = key }))
                    {
                        activate.target = scope.LeftLabel;
                        scope.LeftLabel.SendEvent(activate);
                        if (CatalogInputSettings.IsUnitySubmitKey(key))
                        {
                            Assert.That(activate.isImmediatePropagationStopped, "Unbound native keys must not reach a later submit handler.");
                            Assert.That(activate.imguiEvent.type, Is.EqualTo(EventType.Used), "Consume the backing native submit key too.");
                        }
                    }
                    yield return new WaitForSecondsRealtime(0.2f);
                    Assert.That(config.logic, Is.EqualTo(key == KeyCode.F6 ? ProcessorLogic.All : ProcessorLogic.Filtered), key.ToString());
                }
                using (
                    KeyDownEvent modified = KeyDownEvent.GetPooled(
                        new Event
                        {
                            type = EventType.KeyDown,
                            keyCode = KeyCode.F6,
                            modifiers = EventModifiers.Alt,
                        }
                    )
                )
                {
                    scope.RightLabel.SendEvent(modified);
                }
                Assert.That(config.logic, Is.EqualTo(ProcessorLogic.All), "Selection modifiers are exact.");
                DataVisualizerInputProfile replacement = ScriptableObject.CreateInstance<DataVisualizerInputProfile>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(replacement));
                replacement.Bindings.Navigation.SelectKey = KeyCode.F7;
                replacement.Bindings.Navigation.AlternateSelectKey = KeyCode.None;
                replacement.Bindings.Navigation.ToggleKey = KeyCode.None;
                replacement.Bindings.Navigation.PointerButton = 1;
                state.Settings.inputProfile = replacement;
                scope.RightLabel.Focus();
                SendLabelToggleKey(scope.RightLabel, KeyCode.F6);
                Assert.That(config.logic, Is.EqualTo(ProcessorLogic.All));
                SendLabelToggleKey(scope.RightLabel, KeyCode.F7);
                yield return new WaitForSecondsRealtime(0.2f);
                Assert.That(config.logic, Is.EqualTo(ProcessorLogic.Filtered));
                replacement.Bindings.Navigation.SelectKey = KeyCode.None;
                SendLabelToggleKey(scope.LeftLabel, KeyCode.F7);
                Assert.That(config.logic, Is.EqualTo(ProcessorLogic.Filtered), "None disables activation.");
                foreach (int button in new[] { 0, 1 })
                {
                    Event mouse = new()
                    {
                        type = EventType.MouseDown,
                        button = button,
                        mousePosition = scope.LeftLabel.worldBound.center,
                        clickCount = 1,
                    };
                    using (PointerDownEvent down = PointerDownEvent.GetPooled(mouse))
                    {
                        scope.LeftLabel.SendEvent(down);
                    }
                    yield return null;
                    Assert.That(config.logic, Is.EqualTo(ProcessorLogic.Filtered), "Pressing a scope choice must wait for the completed click.");
                    mouse.type = EventType.MouseUp;
                    using (PointerUpEvent up = PointerUpEvent.GetPooled(mouse))
                    {
                        scope.LeftLabel.SendEvent(up);
                    }
                    yield return new WaitForSecondsRealtime(0.2f);
                    Assert.That(config.logic, Is.EqualTo(button == 1 ? ProcessorLogic.All : ProcessorLogic.Filtered));
                }
                scope.SelectRight(animate: false);
                using (
                    PointerDownEvent down = PointerDownEvent.GetPooled(
                        new Event
                        {
                            type = EventType.MouseDown,
                            button = replacement.Bindings.Navigation.PointerButton,
                            mousePosition = scope.LeftLabel.worldBound.center,
                        }
                    )
                )
                {
                    scope.LeftLabel.SendEvent(down);
                }
                yield return null;
                Assert.That(scope.LeftLabel.HasPointerCapture(PointerId.mousePointerId), "The native click gesture must be active before checking cancellation.");
                using (
                    PointerUpEvent up = PointerUpEvent.GetPooled(
                        new Event
                        {
                            type = EventType.MouseUp,
                            button = replacement.Bindings.Navigation.PointerButton,
                            mousePosition = scope.LeftLabel.worldBound.position - new Vector2(20, 20),
                        }
                    )
                )
                {
                    scope.LeftLabel.SendEvent(up);
                }
                yield return null;
                Assert.That(config.logic, Is.EqualTo(ProcessorLogic.Filtered), "Releasing outside a choice cancels the click.");
                Assert.That(scope.LeftLabel.HasPointerCapture(PointerId.mousePointerId), Is.False);
                Toggle selected = window.rootVisualElement.Q<Toggle>("processor-selected-scope");
                selected.value = true;
                Assert.That(config.logic, Is.EqualTo(ProcessorLogic.Selected));
                Assert.That(scope.enabledInHierarchy, Is.False);
                replacement.Bindings.Navigation.SelectKey = KeyCode.F7;
                SendLabelToggleKey(scope.RightLabel, KeyCode.F7);
                Assert.That(config.logic, Is.EqualTo(ProcessorLogic.Selected), "Disabled scope cannot replace SELECTED.");
                selected.value = false;
                yield return new WaitForSecondsRealtime(0.2f);
                Assert.That(scope.enabledInHierarchy);
                Assert.That(config.logic, Is.EqualTo(ProcessorLogic.Filtered));
                DataVisualizerThemeSelection theme = new();
                cleanup.Defer(() => theme.Apply(null, null));
                foreach (string name in new[] { "Dx", "Classic", "Nord", "Dracula", "Compact", "Minimal" })
                {
                    theme.Apply(
                        window.rootVisualElement,
                        AssetDatabase.LoadAssetAtPath<DataVisualizerThemeSettings>("Packages/com.wallstop-studios.data-visualizer/Editor/DataVisualizer/Styles/" + name + ".asset")
                    );
                    scope.RightLabel.Focus();
                    yield return null;
                    EditorSurfaceCapture.SettleLayout(window);
                    Color unfocused = scope.LeftLabel.resolvedStyle.borderBottomColor;
                    scope.LeftLabel.Focus();
                    yield return null;
                    EditorSurfaceCapture.SettleLayout(window);
                    Assert.That(scope.LeftLabel.resolvedStyle.borderBottomColor, Is.Not.EqualTo(unfocused), name + " focus changes the border color.");
                    Assert.That(scope.LeftLabel.resolvedStyle.borderBottomWidth, Is.Positive, name + " focus must be visible.");
                    Assert.That(scope.worldBound.height, Is.LessThanOrEqualTo(32), name + " retains the compact scope.");
                    using TooltipEvent hint = TooltipEvent.GetPooled();
                    hint.target = scope.LeftLabel;
                    scope.LeftLabel.SendEvent(hint);
                    Assert.That(hint.tooltip, Does.Contain("ALL"));
                    Assert.That(hint.tooltip, Does.Contain("F7"));
                }
                collapse.Focus();
                SendLabelToggleKey(collapse, KeyCode.Return);
                Assert.That(config.isCollapsed, Is.False, "The old physical key cannot toggle the section after a remap.");
                SendLabelToggleKey(collapse, KeyCode.F7);
                Assert.That(config.isCollapsed, Is.True);
                SendLabelToggleKey(collapse, KeyCode.F7);
                Assert.That(config.isCollapsed, Is.False, "The same focused control expands after collapsing its content.");
                replacement.Bindings.Navigation.SelectKey = KeyCode.None;
                SendLabelToggleKey(collapse, KeyCode.F7);
                Assert.That(config.isCollapsed, Is.False, "None disables section activation.");
                using (TooltipEvent hint = TooltipEvent.GetPooled())
                {
                    hint.target = collapse;
                    collapse.SendEvent(hint);
                    Assert.That(hint.tooltip, Does.Contain("Processors"));
                    Assert.That(hint.tooltip, Does.Contain("None"));
                }
                foreach (int button in new[] { 0, 1 })
                {
                    Assert.That(collapse.worldBound.Overlaps(window.rootVisualElement.worldBound));
                    foreach (EventType eventType in new[] { EventType.MouseDown, EventType.MouseUp })
                    {
                        Event mouse = new()
                        {
                            type = eventType,
                            button = button,
                            mousePosition = collapse.worldBound.center,
                        };
                        if (eventType == EventType.MouseDown)
                        {
                            using PointerDownEvent down = PointerDownEvent.GetPooled(mouse);
                            collapse.SendEvent(down);
                        }
                        else
                        {
                            using PointerUpEvent up = PointerUpEvent.GetPooled(mouse);
                            collapse.SendEvent(up);
                        }
                        yield return null;
                    }
                    yield return null;
                    Assert.That(config.isCollapsed, Is.EqualTo(button == 1), "Section collapse follows the current pointer binding.");
                }
                replacement.Bindings.Navigation.SelectKey = KeyCode.F7;
                SendLabelToggleKey(collapse, KeyCode.F7);
                Assert.That(config.isCollapsed, Is.False);
                SuspendForPlayMode(window);
                InvokePrivate(window, "RunDataProcessor", scope, new EditorProcessorAutomationTests.JsonProbeProcessor<int>());
                Assert.That(config.logic, Is.EqualTo(ProcessorLogic.Filtered), "A blocked processor must preserve the chosen scope.");
                ResumeFromPlayMode(window);
                yield return WaitForRefresh(window);
                DataVisualizerUserState persisted = new();
                if (projectState)
                {
                    persisted.HydrateFrom(state.Settings);
                }
                else
                {
                    persisted = DataVisualizerUserState.FromJson(System.IO.File.ReadAllText(state.FilePath));
                }
                ProcessorState saved = persisted.processorStates.Find(item => item.typeFullName == type.FullName);
                Assert.That(saved.logic, Is.EqualTo(ProcessorLogic.Filtered), "The chosen scope reaches the active persistence store.");
            }
        }

        [UnityTest]
        public IEnumerator ShouldToggleLabelFiltersWithConfiguredKeyboardAndPointer()
        {
            using TestCleanupScope preferenceCleanup = new();
            DocsImageCapture.WindowPreferenceSnapshot preferences = DocsImageCapture.WindowPreferenceSnapshot.Capture();
            preferenceCleanup.Defer(preferences.Restore);
            foreach (bool projectState in new[] { false, true })
            {
                preferences.ApplyWidePanes();
                using TestCleanupScope cleanup = new();
                object previousInstance = ReadSharedInstance();
                cleanup.Defer(() => RestoreSharedInstance(previousInstance));
                cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
                DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
                string output = "Temp/LabelToggle-" + Guid.NewGuid().ToString("N");
                cleanup.Defer(() => System.IO.Directory.Delete(output, recursive: true));
                using PerfWindowStateScope state = new(settings, new DataVisualizerUserState(), output);
                state.Settings.persistStateInSettingsAsset = projectState;
                Type type = typeof(TestDataObject);
                state.UserState.managedTypeNames.Add(type.FullName);
                state.Settings.HydrateFrom(state.UserState);
                string folder = CreateFixtureFolder();
                cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
                ScriptableObject labelled = CreateFixtureAsset(folder, "Labelled.asset");
                AssetDatabase.SetLabels(labelled, new[] { "Toggle fixture" });
                DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
                using TestCleanupScope windowCleanup = new();
                windowCleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
                SetPrivateField(window, "_settings", state.Settings);
                SetPrivateField(window, "_userState", state.UserState);
                SetPrivateField(window, "_userStateFilePath", state.FilePath);
                EditorSurfaceCapture.ShowPopup(window);
                SetPrivateField(window, "_settings", state.Settings);
                SetPrivateField(window, "_userState", state.UserState);
                SetPrivateField(window, "_userStateFilePath", state.FilePath);
                window.position = new Rect(40, 40, projectState ? 760 : 1180, 780);
                window.CreateGUI();
                InvokePrivate(window, "LoadInitialContent");
                ReadPrivateField<NamespaceController>(window, "_namespaceController").SelectType(window, type);
                yield return WaitForRefresh(window);
                if (projectState)
                {
                    InvokePrivate(window, "ShowInspectorIfCompact");
                }
                TypeLabelFilterConfig config = (TypeLabelFilterConfig)InvokePrivate(window, "LoadOrCreateLabelFilterConfig", type);
                Assert.That(config != null);
                config.andLabels.Clear();
                config.orLabels.Clear();
                InvokePrivate(window, "ToggleLabelsCollapsed", true);
                InvokePrivate(window, "ShowObjectPaneIfCompact");
                yield return null;
                EditorSurfaceCapture.SettleLayout(window);
                Button toggle = window.rootVisualElement.Q<Button>("label-filter-collapse-toggle");
                Assert.That(toggle != null, "Label controls need a native focusable button.");
                Assert.That(toggle.focusable);
                string layout = "window=" + window.position;
                for (VisualElement ancestor = toggle; ancestor != null; ancestor = ancestor.parent)
                {
                    layout += " / " + ancestor.name + ":" + ancestor.resolvedStyle.display + ":" + ancestor.worldBound;
                }
                Assert.That(toggle.worldBound.height, Is.Positive, layout);
                DataVisualizerThemeSelection selection = new();
                cleanup.Defer(() => selection.Apply(null, null));
                foreach (string themeName in new[] { "Dx", "Classic", "Nord", "Dracula", "Compact", "Minimal" })
                {
                    selection.Apply(
                        window.rootVisualElement,
                        AssetDatabase.LoadAssetAtPath<DataVisualizerThemeSettings>(
                            "Packages/com.wallstop-studios.data-visualizer/Editor/DataVisualizer/Styles/" + themeName + ".asset"
                        )
                    );
                    yield return null;
                    toggle.Focus();
                    EditorSurfaceCapture.SettleLayout(window);
                    Assert.That(toggle.worldBound.height, Is.EqualTo(22).Within(1), themeName);
                    Assert.That(toggle.worldBound.width, Is.EqualTo(22).Within(1), themeName);
                    Assert.That(toggle.resolvedStyle.paddingLeft, Is.Zero, themeName);
                    Assert.That(toggle.resolvedStyle.borderLeftWidth, Is.Positive, themeName + " focus is visible");
                }
                toggle.Focus();
                yield return null;
                Assert.That(window.rootVisualElement.focusController.focusedElement, Is.SameAs(toggle));
                SendLabelToggleKey(toggle, KeyCode.Return);
                Assert.That(config.isCollapsed, Is.False, "The default submit key opens the label filter.");
                DataVisualizerInputProfile profile = ScriptableObject.CreateInstance<DataVisualizerInputProfile>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(profile));
                state.Settings.inputProfile = profile;
                profile.Bindings.Navigation.SelectKey = KeyCode.F6;
                profile.Bindings.Navigation.AlternateSelectKey = KeyCode.None;
                profile.Bindings.Navigation.ToggleKey = KeyCode.None;
                using (TooltipEvent hint = TooltipEvent.GetPooled())
                {
                    hint.target = toggle;
                    toggle.SendEvent(hint);
                    Assert.That(hint.tooltip, Does.Contain("F6"));
                }
                SendLabelToggleKey(toggle, KeyCode.Return);
                Assert.That(config.isCollapsed, Is.False, "The old native submit key must stop working.");
                SendLabelToggleKey(toggle, KeyCode.F6);
                Assert.That(config.isCollapsed);
                profile.Bindings.Navigation.SelectKey = KeyCode.None;
                SendLabelToggleKey(toggle, KeyCode.F6);
                Assert.That(config.isCollapsed, "An unbound submit key must stop working.");
                DataVisualizerInputProfile replacement = ScriptableObject.CreateInstance<DataVisualizerInputProfile>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(replacement));
                replacement.Bindings.Navigation.SelectKey = KeyCode.F7;
                replacement.Bindings.Navigation.AlternateSelectKey = KeyCode.None;
                replacement.Bindings.Navigation.ToggleKey = KeyCode.None;
                replacement.Bindings.Navigation.PointerButton = 1;
                state.Settings.inputProfile = replacement;
                SendLabelToggleKey(toggle, KeyCode.F7);
                Assert.That(config.isCollapsed, Is.False, "Profile replacement takes effect on the existing button.");
                foreach (int button in new[] { 0, 1 })
                {
                    using (
                        PointerDownEvent down = PointerDownEvent.GetPooled(
                            new Event
                            {
                                type = EventType.MouseDown,
                                button = button,
                                mousePosition = toggle.worldBound.center,
                            }
                        )
                    )
                    {
                        toggle.SendEvent(down);
                    }
                    yield return null;
                    using (
                        PointerUpEvent up = PointerUpEvent.GetPooled(
                            new Event
                            {
                                type = EventType.MouseUp,
                                button = button,
                                mousePosition = toggle.worldBound.center,
                            }
                        )
                    )
                    {
                        toggle.SendEvent(up);
                    }
                    yield return null;
                    Assert.That(config.isCollapsed, Is.EqualTo(button == 1), "Only the configured pointer button toggles the section.");
                }
                config.andLabels.Add("Keep visible");
                InvokePrivate(window, "UpdateLabelAreaAndFilter");
                Assert.That(toggle.enabledInHierarchy, Is.False, "An active filter cannot be hidden.");
                Assert.That(window.rootVisualElement.Q<LabelFilterEditor>().style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(toggle.tooltip, Does.Contain("Clear"));
            }
        }

        [UnityTest]
        public IEnumerator ShouldRefreshOnlyChangedRealizedAssetRows()
        {
            using TestCleanupScope cleanup = new();
            string folder = CreateFixtureFolder();
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            object previousInstance = ReadSharedInstance();
            cleanup.Defer(() => RestoreSharedInstance(previousInstance));
            cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
            DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
            string output = "Temp/TargetedRowRefresh-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() => System.IO.Directory.Delete(output, recursive: true));
            using PerfWindowStateScope state = new(settings, new DataVisualizerUserState(), output);
            Type type = typeof(TestDataObject);
            state.UserState.managedTypeNames.Add(type.FullName);
            state.UserState.GetOrCreateTypeOrderList(NamespaceController.GetNamespaceKey(type)).Add(type.FullName);
            ScriptableObject changed = CreateFixtureAsset(folder, "Changed.asset");
            ScriptableObject unchanged = CreateFixtureAsset(folder, "Other.asset");
            AssetDatabase.SetLabels(changed, new[] { "Inspector stable label" });
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            using TestCleanupScope windowCleanup = new();
            windowCleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
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
            ReadPrivateField<NamespaceController>(window, "_namespaceController").SelectType(window, type);
            yield return WaitForRefresh(window);
            EditorSurfaceCapture.SettleLayout(window);
            DataVisualizerThemeSelection labelTheme = new();
            cleanup.Defer(() => labelTheme.Apply(null, null));
            foreach (string themeName in new[] { "Dx", "Classic", "Nord", "Dracula", "Compact", "Minimal" })
            {
                labelTheme.Apply(
                    window.rootVisualElement,
                    AssetDatabase.LoadAssetAtPath<DataVisualizerThemeSettings>("Packages/com.wallstop-studios.data-visualizer/Editor/DataVisualizer/Styles/" + themeName + ".asset")
                );
                foreach (float width in new[] { 1180f, 760f })
                {
                    window.position = new Rect(40, 40, width, 780);
                    yield return null;
                    InvokePrivate(window, "ShowInspectorIfCompact");
                    EditorSurfaceCapture.SettleLayout(window);
                    TextField field = window.rootVisualElement.Q<TextField>("inspector-new-label-input");
                    Button add = window.rootVisualElement.Q<Button>("inspector-add-label-button");
                    Assert.That(field != null && add != null);
                    Assert.That(field.worldBound.height, Is.EqualTo(add.worldBound.height).Within(1), themeName + " entry controls share a height.");
                    Assert.That(field.Q("unity-text-input").worldBound.height, Is.EqualTo(add.worldBound.height).Within(1), themeName + " text input is not a short strip.");
                    Assert.That(field.worldBound.y, Is.EqualTo(add.worldBound.y).Within(1));
                    Assert.That(field.worldBound.width, Is.GreaterThan(add.worldBound.width));
                }
            }
            window.position = new Rect(40, 40, 1180, 780);
            yield return null;
            EditorSurfaceCapture.SettleLayout(window);
            InvokePrivate(window, "SetActiveObject", changed, false);
            EditorSurfaceCapture.SettleLayout(window);
            VisualElement inspectorLabels = ReadPrivateField<VisualElement>(window, "_inspectorCurrentLabelsContainer");
            VisualElement retainedPill = inspectorLabels[0];
            Button retainedRemove = retainedPill.Q<Button>();
            retainedRemove.Focus();
            Assert.That(window.rootVisualElement.focusController.focusedElement, Is.SameAs(retainedRemove));
            ListView list = ReadPrivateField<ListView>(window, "_objectListView");
            List<ScriptableObject> filtered = ReadPrivateField<List<ScriptableObject>>(window, "_filteredObjects");
            int changedIndex = filtered.IndexOf(changed);
            Assert.That(changedIndex, Is.Not.Negative);
            list.ScrollToItem(changedIndex);
            VisualElement changedRow = null;
            for (int tick = 0; tick < 60; tick++)
            {
                yield return null;
                EditorSurfaceCapture.SettleLayout(window);
                changedRow = list.Query<VisualElement>().Where(element => ReferenceEquals(element.userData, changed)).First();
                if (changedRow != null)
                {
                    break;
                }
            }
            Assert.That(changedRow != null, "The uniquely owned fixture must be realized before editing.");
            AssertRowOrderingControls(changedRow, canMoveUp: 0 < changedIndex, canMoveDown: changedIndex < filtered.Count - 1);
            Assert.That(AssetDatabase.GetAssetPath(changed).StartsWith(folder + "/", StringComparison.Ordinal));
            string path = AssetDatabase.GetAssetPath(changed);
            ScriptableObject selected = ReadPrivateField<ScriptableObject>(window, "_selectedObject");
            int generation = ReadPrivateField<int>(window, "_asyncLoadGeneration");
            int originalCount = list.itemsSource.Count;
            float offset = list.Q<ScrollView>().scrollOffset.y;
            int bindings = 0;
            Action<VisualElement, int> bind = list.bindItem;
            list.bindItem = (element, index) =>
            {
                bindings++;
                bind(element, index);
            };
            bindings = 0;
            int refreshLoads = ReadPrivateField<int>(window, "_assetRefreshObjectLoadCount");
            string changedGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(changed));
            string unchangedGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(unchanged));
            Assert.That(AssetGuidTypeIndex.Shared.Metadata.TryGet(unchangedGuid, out AssetMetadataRecord unchangedMetadata));
            string originalTitle = ((TestDataObject)changed).Title;
            cleanup.Defer(() =>
            {
                if (changed != null)
                {
                    Undo.ClearUndo(changed);
                }
            });
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Edit owned fixture title");
            using (SerializedObject inspector = new(changed))
            {
                inspector.FindProperty("_title").stringValue = "Inspector title";
                Assert.That(inspector.ApplyModifiedProperties(), "The inspector edit must change serialized data.");
            }
            Undo.FlushUndoRecordObjects();
            for (int operation = 0; operation < 3; operation++)
            {
                string expectedTitle = operation == 1 ? originalTitle : "Inspector title";
                if (operation == 1)
                {
                    Undo.PerformUndo();
                }
                else if (operation == 2)
                {
                    Undo.PerformRedo();
                }
                Assert.That(((TestDataObject)changed).Title, Is.EqualTo(expectedTitle), "The edit/Undo/Redo must actually change the fixture.");
                for (int tick = 0; tick < 300 && changedRow.Q<Label>("object-item-label").text != expectedTitle; tick++)
                {
                    yield return null;
                }
                Assert.That(changedRow.Q<Label>("object-item-label").text, Is.EqualTo(expectedTitle), "Object-change events must refresh the row without a manual queue call.");
                yield return WaitForRefresh(window);
                Assert.That(AssetGuidTypeIndex.Shared.Metadata.TryGet(changedGuid, out AssetMetadataRecord updatedMetadata));
                Assert.That(updatedMetadata.DisplayTitle, Is.EqualTo(expectedTitle), "Shared display metadata follows the same edit history.");
                Assert.That(AssetGuidTypeIndex.Shared.Metadata.TryGet(unchangedGuid, out AssetMetadataRecord retainedMetadata));
                Assert.That(retainedMetadata, Is.SameAs(unchangedMetadata), "Unchanged assets keep their original metadata record.");
                Assert.That(inspectorLabels[0], Is.SameAs(retainedPill), "Non-label edits must keep existing inspector controls.");
                Assert.That(window.rootVisualElement.focusController.focusedElement, Is.SameAs(retainedRemove), "Refresh must preserve label-action focus.");
                Assert.That(bindings, Is.Zero, "Inspector edits and Undo/Redo must not rebind unrelated rows.");
                Assert.That(ReadPrivateField<int>(window, "_assetRefreshObjectLoadCount"), Is.EqualTo(refreshLoads), "Already loaded assets must not reload.");
                Assert.That(list.itemsSource.Count, Is.EqualTo(originalCount));
                Assert.That(ReadPrivateField<ScriptableObject>(window, "_selectedObject") == selected);
                Assert.That(ReadPrivateField<int>(window, "_asyncLoadGeneration"), Is.EqualTo(generation));
                Assert.That(list.Q<ScrollView>().scrollOffset.y, Is.EqualTo(offset).Within(1));
            }
            Undo.ClearUndo(changed);
            ((TestDataObject)changed).Title = "Changed row";
            Assert.That(AssetDatabase.RenameAsset(path, "Changed row"), Is.Empty);
            string renamed = AssetDatabase.GetAssetPath(changed);
            window.QueueAssetChanges(new[] { renamed }, null, null, null);
            yield return WaitForRefresh(window);
            EditorSurfaceCapture.SettleLayout(window);
            Label refreshedTitle = changedRow.Q<Label>("object-item-label");
            Assert.That(refreshedTitle.text, Is.EqualTo("Changed row"));
            refreshedTitle.style.maxWidth = 16;
            yield return null;
            EditorSurfaceCapture.SettleLayout(window);
            Assert.That(refreshedTitle.worldBound.width, Is.LessThanOrEqualTo(16));
            refreshedTitle.style.maxWidth = StyleKeyword.Null;
            yield return null;
            EditorSurfaceCapture.SettleLayout(window);
            Assert.That(refreshedTitle.worldBound.width, Is.GreaterThan(16));
            Assert.That(refreshedTitle.tooltip, Is.EqualTo("Changed row"), "Native elision updates must not clear the explicit asset-title hint.");
            Assert.That(changedRow.Q<Button>("go-up-button").tooltip, Does.Contain("Changed row"));
            Assert.That(bindings, Is.Zero, "A nonstructural edit must not rebind unchanged rows.");
            Assert.That(list.itemsSource.Count, Is.EqualTo(originalCount));
            Assert.That(ReadPrivateField<ScriptableObject>(window, "_selectedObject") == selected);
            Assert.That(ReadPrivateField<int>(window, "_asyncLoadGeneration"), Is.EqualTo(generation));
            Assert.That(list.Q<ScrollView>().scrollOffset.y, Is.EqualTo(offset).Within(1));
            string filterLabel = "Targeted-row-" + Guid.NewGuid().ToString("N");
            AssetDatabase.SetLabels(changed, new[] { filterLabel });
            window.QueueAssetChanges(new[] { renamed }, null, null, null);
            yield return WaitForRefresh(window);
            Assert.That(bindings, Is.Zero, "Label edits without membership changes keep row bindings.");
            InvokePrivate(window, "ToggleLabelsCollapsed", false);
            yield return null;
            EditorSurfaceCapture.SettleLayout(window);
            VisualElement labelPills = window.rootVisualElement.Q("simple-label-pills");
            Button focusedLabel = labelPills.Query<Button>().Where(button => string.Equals(button.userData as string, filterLabel, StringComparison.Ordinal)).First();
            Assert.That(focusedLabel != null);
            focusedLabel.Focus();
            yield return null;
            Assert.That(window.rootVisualElement.focusController.focusedElement, Is.SameAs(focusedLabel));
            AssetDatabase.SetLabels(changed, new[] { filterLabel, filterLabel + "-new" });
            window.QueueAssetChanges(new[] { renamed }, null, null, null);
            yield return WaitForRefresh(window);
            Assert.That(focusedLabel.parent, Is.SameAs(labelPills), "Incremental label refresh preserves surviving controls.");
            Assert.That(window.rootVisualElement.focusController.focusedElement, Is.SameAs(focusedLabel), "Asset edits must not steal filter focus.");
            Assert.That(bindings, Is.Zero);
            TypeLabelFilterConfig filter = state.UserState.labelFilterConfigs.Find(config => config.typeFullName == type.FullName);
            filter.andLabels.Add(filterLabel);
            InvokePrivate(window, "ApplyLabelFilter", true);
            yield return null;
            Assert.That(filtered.Contains(changed));
            Assert.That(filtered.Count, Is.EqualTo(1), "The uniquely labeled asset is the only visible row.");
            changedRow = list.Query<VisualElement>().Where(element => ReferenceEquals(element.userData, changed)).First();
            Assert.That(changedRow != null);
            AssertRowOrderingControls(changedRow, canMoveUp: false, canMoveDown: false);
            AssetDatabase.SetLabels(changed, Array.Empty<string>());
            window.QueueAssetChanges(new[] { renamed }, null, null, null);
            yield return WaitForRefresh(window);
            Assert.That(filtered.Contains(changed), Is.False, "Label edits that hide a row must refresh membership.");
            AssetDatabase.SetLabels(changed, new[] { filterLabel });
            window.QueueAssetChanges(new[] { renamed }, null, null, null);
            yield return WaitForRefresh(window);
            Assert.That(filtered.Contains(changed), Is.True, "Label edits that reveal a row must refresh membership.");
            filter.andLabels.Clear();
            InvokePrivate(window, "ApplyLabelFilter", true);
            yield return null;
            InvokePrivate(window, "MoveObjectToTop", changed);
            yield return null;
            yield return null;
            EditorSurfaceCapture.SettleLayout(window);
            changedRow = list.Query<VisualElement>().Where(element => ReferenceEquals(element.userData, changed)).First();
            Assert.That(changedRow != null);
            AssertRowOrderingControls(changedRow, canMoveUp: false, canMoveDown: true);
            InvokePrivate(window, "MoveObjectToBottom", changed);
            yield return null;
            yield return null;
            EditorSurfaceCapture.SettleLayout(window);
            changedRow = list.Query<VisualElement>().Where(element => ReferenceEquals(element.userData, changed)).First();
            Assert.That(changedRow != null);
            AssertRowOrderingControls(changedRow, canMoveUp: true, canMoveDown: false);
            bindings = 0;
            window.QueueAssetChanges(new[] { renamed }, null, null, null);
            yield return WaitForRefresh(window);
            AssertRowOrderingControls(changedRow, canMoveUp: true, canMoveDown: false);
            Assert.That(AssetDatabase.DeleteAsset(renamed));
            window.QueueAssetChanges(null, new[] { renamed }, null, null);
            yield return WaitForRefresh(window);
            EditorSurfaceCapture.SettleLayout(window);
            Assert.That(list.itemsSource.Count, Is.EqualTo(originalCount - 1));
            Assert.That(bindings, Is.Positive, "Deletion must reconcile the changed list structure.");
        }

        [UnityTest]
        public IEnumerator ShouldReconcileActualPlayModeAssetChangesWithoutDomainReload()
        {
            using TestCleanupScope cleanup = new();
            bool previousOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            EnterPlayModeOptions previousOptions = EditorSettings.enterPlayModeOptions;
            cleanup.Defer(() =>
            {
                EditorSettings.enterPlayModeOptions = previousOptions;
                EditorSettings.enterPlayModeOptionsEnabled = previousOptionsEnabled;
            });
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            string folder = CreateFixtureFolder();
            cleanup.Defer(() => AssetDatabase.DeleteAsset(folder));
            object previousInstance = ReadSharedInstance();
            cleanup.Defer(() => RestoreSharedInstance(previousInstance));
            cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
            DataVisualizerSettings settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(settings));
            string output = "Temp/PlayRefresh-" + Guid.NewGuid().ToString("N");
            cleanup.Defer(() => System.IO.Directory.Delete(output, recursive: true));
            using PerfWindowStateScope state = new(settings, new DataVisualizerUserState(), output);
            Type type = typeof(TestDataObject);
            state.UserState.managedTypeNames.Add(type.FullName);
            state.UserState.GetOrCreateTypeOrderList(NamespaceController.GetNamespaceKey(type)).Add(type.FullName);
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            using TestCleanupScope windowCleanup = new();
            windowCleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            SetPrivateField(window, "_settings", state.Settings);
            SetPrivateField(window, "_userState", state.UserState);
            SetPrivateField(window, "_userStateFilePath", state.FilePath);
            EditorSurfaceCapture.ShowPopup(window);
            SetPrivateField(window, "_settings", state.Settings);
            SetPrivateField(window, "_userState", state.UserState);
            SetPrivateField(window, "_userStateFilePath", state.FilePath);
            window.CreateGUI();
            ScriptableObject moved = CreateFixtureAsset(folder, "Moved.asset");
            CreateFixtureAsset(folder, "Deleted.asset");
            TestDataObject edited = (TestDataObject)CreateFixtureAsset(folder, "Edited.asset");
            InvokePrivate(window, "LoadInitialContent");
            NamespaceController controller = ReadPrivateField<NamespaceController>(window, "_namespaceController");
            controller.SelectType(window, type);
            yield return WaitForRefresh(window);
            InvokePrivate(window, "SelectObject", moved);
            string movedGuid = AssetDatabase.AssetPathToGUID(folder + "/Moved.asset");
            string deletedGuid = AssetDatabase.AssetPathToGUID(folder + "/Deleted.asset");
            string editedGuid = AssetDatabase.AssetPathToGUID(folder + "/Edited.asset");
            AssetObjectCache cache = ReadPrivateField<AssetObjectCache>(window, "_assetObjectCache");
            Assert.That(cache.Contains(movedGuid));
            Assert.That(cache.Contains(deletedGuid));
            Assert.That(cache.Contains(editedGuid));
            int originalRows = ReadPrivateField<List<ScriptableObject>>(window, "_selectedObjects").Count;
            int generation = ReadPrivateField<int>(window, "_asyncLoadGeneration");
            yield return new EnterPlayMode(false);
            Assert.That(EditorApplication.isPlaying);
            Assert.That(ReadPrivateField<bool>(window, "_suspendedForPlayMode"));
            Assert.That(window.rootVisualElement.Q<Button>("create-object-button").enabledSelf, Is.False);
            Assert.That(AssetDatabase.MoveAsset(folder + "/Moved.asset", folder + "/Renamed.asset"), Is.Empty);
            Assert.That(AssetDatabase.DeleteAsset(folder + "/Deleted.asset"));
            edited.Description = "changedduringactualplay";
            AssetDatabase.SetLabels(edited, new[] { "DuringPlay" });
            EditorUtility.SetDirty(edited);
            AssetDatabase.SaveAssets();
            ScriptableObject added = CreateFixtureAsset(folder, "Added.asset");
            string addedGuid = AssetDatabase.AssetPathToGUID(folder + "/Added.asset");
            for (int tick = 0; tick < 5; tick++)
            {
                yield return null;
            }
            Assert.That(cache.Contains(deletedGuid), "The suspended window must keep its pre-play snapshot.");
            Assert.That(!cache.Contains(addedGuid));
            yield return new ExitPlayMode();
            Assert.That(!EditorApplication.isPlaying);
            yield return WaitForRefresh(window);
            Assert.That(!ReadPrivateField<bool>(window, "_suspendedForPlayMode"));
            Assert.That(window.rootVisualElement.Q<Button>("create-object-button").enabledSelf);
            Assert.That(cache.Contains(movedGuid));
            Assert.That(!cache.Contains(deletedGuid));
            Assert.That(cache.Contains(addedGuid));
            Assert.That(cache.TryGetDocument(editedGuid, out var document));
            Assert.That(document.Match(new[] { "changedduringactualplay" }).isMatch);
            Assert.That(AssetGuidTypeIndex.Shared.Metadata.TryGet(editedGuid, out AssetMetadataRecord metadata));
            Assert.That(metadata.Labels, Does.Contain("DuringPlay"));
            Assert.That(ReadPrivateField<ScriptableObject>(window, "_selectedObject") == moved);
            Assert.That(ReadPrivateField<List<ScriptableObject>>(window, "_selectedObjects"), Does.Contain(added));
            Assert.That(ReadPrivateField<List<ScriptableObject>>(window, "_selectedObjects").Count, Is.EqualTo(originalRows));
            Assert.That(ReadPrivateField<int>(window, "_asyncLoadGeneration"), Is.EqualTo(generation), "Routine changes must preserve row loading generations.");
        }

        [UnityTearDown]
        public IEnumerator RestoreEditModeAfterFailedTransition()
        {
            if (EditorApplication.isPlaying)
            {
                yield return new ExitPlayMode();
            }
        }

        [Test]
        public void ShouldGateDeleteWhileSuspendedForPlayMode()
        {
            string folder = CreateFixtureFolder();
            object previousInstance = ReadSharedInstance();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() =>
                {
                    AssetDatabase.DeleteAsset(folder);
                    AssetDatabase.Refresh();
                });
                cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
                cleanup.Defer(() => RestoreSharedInstance(previousInstance));

                DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));

                ScriptableObject asset = CreateFixtureAsset(folder, "First.asset");
                SetPrivateField(window, "_popoverContext", asset);

                SuspendForPlayMode(window);
                InvokePrivate(window, "HandleDeleteConfirmed");
                Assert.That(AssetDatabase.LoadAssetAtPath<ScriptableObject>(folder + "/First.asset") != null, "a delete confirmed while suspended must not delete the asset");

                ResumeFromPlayMode(window);
                InvokePrivate(window, "HandleDeleteConfirmed");
                Assert.That(AssetDatabase.LoadAssetAtPath<ScriptableObject>(folder + "/First.asset") == null, "the same delete after resume must delete the asset");
            }
        }

        [Test]
        public void ShouldGateCloneWhileSuspendedForPlayMode()
        {
            string folder = CreateFixtureFolder();
            object previousInstance = ReadSharedInstance();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() =>
                {
                    AssetDatabase.DeleteAsset(folder);
                    AssetDatabase.Refresh();
                });
                cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
                cleanup.Defer(() => RestoreSharedInstance(previousInstance));

                DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));

                ScriptableObject asset = CreateFixtureAsset(folder, "First.asset");
                ReadPrivateField<List<ScriptableObject>>(window, "_selectedObjects").Add(asset);
                ReadPrivateField<List<ScriptableObject>>(window, "_filteredObjects").Add(asset);

                SuspendForPlayMode(window);
                InvokePrivate(window, "CloneObject", asset);
                Assert.That(AssetDatabase.LoadAssetAtPath<ScriptableObject>(folder + "/First (Clone).asset") == null, "cloning must not create an asset while suspended");

                ResumeFromPlayMode(window);
                InvokePrivate(window, "CloneObject", asset);
                Assert.That(AssetDatabase.LoadAssetAtPath<ScriptableObject>(folder + "/First (Clone).asset") != null, "cloning after resume must create the clone");
            }
        }

        [Test]
        public void ShouldGateRenameWhileSuspendedForPlayMode()
        {
            string folder = CreateFixtureFolder();
            object previousInstance = ReadSharedInstance();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() =>
                {
                    AssetDatabase.DeleteAsset(folder);
                    AssetDatabase.Refresh();
                });
                cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
                cleanup.Defer(() => RestoreSharedInstance(previousInstance));

                DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));

                CreateFixtureAsset(folder, "First.asset");
                SetPrivateField(window, "_popoverContext", folder + "/First.asset");

                SuspendForPlayMode(window);
                InvokePrivate(window, "HandleRenameConfirmed", new Label(), new TextField { value = "Renamed" }, new Label());
                Assert.That(AssetDatabase.LoadAssetAtPath<ScriptableObject>(folder + "/First.asset") != null, "a rename confirmed while suspended must not rename the asset");

                ResumeFromPlayMode(window);
                InvokePrivate(window, "HandleRenameConfirmed", new Label(), new TextField { value = "Renamed" }, new Label());
                Assert.That(AssetDatabase.LoadAssetAtPath<ScriptableObject>(folder + "/First.asset") == null, "the same rename after resume must rename the asset");
                Assert.That(AssetDatabase.LoadAssetAtPath<ScriptableObject>(folder + "/Renamed.asset") != null, "the rename after resume must land at the new path");
            }
        }

        [Test]
        public void ShouldGateLabelRemovalWhileSuspendedForPlayMode()
        {
            string folder = CreateFixtureFolder();
            object previousInstance = ReadSharedInstance();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() =>
                {
                    AssetDatabase.DeleteAsset(folder);
                    AssetDatabase.Refresh();
                });
                cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
                cleanup.Defer(() => RestoreSharedInstance(previousInstance));

                DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));

                ScriptableObject asset = CreateFixtureAsset(folder, "First.asset");
                AssetDatabase.SetLabels(asset, new[] { "GateProbe" });
                SetPrivateField(window, "_selectedObject", asset);

                SuspendForPlayMode(window);
                InvokePrivate(window, "RemoveLabelFromSelectedAsset", "GateProbe");
                CollectionAssert.Contains(AssetDatabase.GetLabels(asset), "GateProbe", "a label removal while suspended must not change the asset labels");

                ResumeFromPlayMode(window);
                InvokePrivate(window, "RemoveLabelFromSelectedAsset", "GateProbe");
                CollectionAssert.DoesNotContain(AssetDatabase.GetLabels(asset), "GateProbe", "the same label removal after resume must remove the label");
            }
        }

        [Test]
        public void ShouldGateManagedTypeAddWhileSuspendedForPlayMode()
        {
            object previousInstance = ReadSharedInstance();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
                cleanup.Defer(() => RestoreSharedInstance(previousInstance));

                DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));

                Dictionary<string, List<Type>> managedTypes = ReadPrivateField<Dictionary<string, List<Type>>>(window, "_scriptableObjectTypes");
                Assert.AreEqual(0, managedTypes.Count);

                List<Type> typesToAdd = new() { typeof(TestDataObject) };

                SuspendForPlayMode(window);
                Assert.IsFalse((bool)InvokePrivate(window, "AddManagedTypes", typesToAdd), "a managed-type add while suspended must not change the catalog");
                Assert.AreEqual(0, managedTypes.Count);

                ResumeFromPlayMode(window);
                Assert.IsTrue((bool)InvokePrivate(window, "AddManagedTypes", typesToAdd), "the same managed-type add after resume must change the catalog");
                Assert.AreEqual(1, managedTypes.Count);
            }
        }

        [Test]
        public void ShouldGateTypeSwitchWhileSuspendedForPlayMode()
        {
            string folder = CreateFixtureFolder();
            object previousInstance = ReadSharedInstance();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() =>
                {
                    AssetDatabase.DeleteAsset(folder);
                    AssetDatabase.Refresh();
                });
                cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
                cleanup.Defer(() => RestoreSharedInstance(previousInstance));

                DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));

                CreateFixtureAsset(folder, "First.asset");
                NamespaceController controller = ReadPrivateField<NamespaceController>(window, "_namespaceController");

                /*
                    SelectType resolves the type's view element before selecting, so the resume
                    control needs the namespace view built; AddManagedTypes seeds the catalog and
                    BuildNamespaceView populates the type-element cache.
                */
                Assert.IsTrue((bool)InvokePrivate(window, "AddManagedTypes", new List<Type> { typeof(TestDataObject) }));
                InvokePrivate(window, "BuildNamespaceView");

                SuspendForPlayMode(window);
                controller.SelectType(window, typeof(TestDataObject));
                Assert.That(controller.SelectedType == null, "a type switch while suspended must not select the type");

                ResumeFromPlayMode(window);
                controller.SelectType(window, typeof(TestDataObject));
                Assert.AreEqual(typeof(TestDataObject), controller.SelectedType, "the same type switch after resume must select the type");
            }
        }

        [Test]
        public void ShouldGateManualTypeLoadWhileSuspendedForPlayMode()
        {
            string folder = CreateFixtureFolder();
            object previousInstance = ReadSharedInstance();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() =>
                {
                    AssetDatabase.DeleteAsset(folder);
                    AssetDatabase.Refresh();
                });
                cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
                cleanup.Defer(() => RestoreSharedInstance(previousInstance));

                DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));

                ScriptableObject fixtureAsset = CreateFixtureAsset(folder, "First.asset");

                SuspendForPlayMode(window);
                InvokePrivate(window, "LoadObjectTypesAsync", typeof(TestDataObject), false);
                Assert.IsFalse(ReadPrivateField<bool>(window, "_isLoadingObjectsAsync"), "a manual type load while suspended must not start the load chain");
                Assert.That(ReadPrivateField<Type>(window, "_asyncLoadTargetType") == null);
                Assert.AreEqual(0, ReadPrivateField<List<ScriptableObject>>(window, "_selectedObjects").Count, "a manual type load while suspended must not load any objects");

                ResumeFromPlayMode(window);
                InvokePrivate(window, "LoadObjectTypesAsync", typeof(TestDataObject), false);
                while (ReadPrivateField<bool>(window, "_isLoadingObjectsAsync"))
                {
                    InvokePrivate(window, "ContinueLoadingObjects", typeof(TestDataObject), ReadPrivateField<int>(window, "_asyncLoadGeneration"));
                }

                CollectionAssert.Contains(
                    ReadPrivateField<List<ScriptableObject>>(window, "_selectedObjects"),
                    fixtureAsset,
                    "the same manual type load after resume must load the fixture even when other assets exist"
                );
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShouldSelectLoadedObjectsWhenResumingInterruptedInitialSlice(bool hasPreviousType)
        {
            string folder = CreateFixtureFolder();
            object previousInstance = ReadSharedInstance();
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() =>
            {
                AssetDatabase.DeleteAsset(folder);
                AssetDatabase.Refresh();
            });
            cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
            cleanup.Defer(() => RestoreSharedInstance(previousInstance));
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));
            ScriptableObject fixtureAsset = CreateFixtureAsset(folder, "First.asset");
            ScriptableObject previous = hasPreviousType ? ScriptableObject.CreateInstance<OtherEditorOnlyCreationData>() : null;
            if (previous != null)
            {
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(previous));
            }
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(fixtureAsset));
            ReadPrivateField<EditorAssetSelection>(window, "processorSelection").SetType(typeof(TestDataObject));
            SetPrivateField(window, "_asyncLoadTargetType", typeof(TestDataObject));
            SetPrivateField(window, "_isLoadingObjectsAsync", true);
            SetPrivateField(window, "_selectedObject", previous);
            // Model suspension after insertion but before the initial slice selects its inspector.
            InvokePrivate(window, "InsertObjectByAsyncDisplayOrder", fixtureAsset, guid);
            ReadPrivateField<Queue<string>>(window, "_pendingObjectGuids").Enqueue(guid);
            SuspendForPlayMode(window);
            ResumeFromPlayMode(window);
            InvokePrivate(window, "ContinueLoadingObjects", typeof(TestDataObject), ReadPrivateField<int>(window, "_asyncLoadGeneration"));
            Assert.That(ReadPrivateField<ScriptableObject>(window, "_selectedObject"), Is.SameAs(fixtureAsset));
            Assert.That(ReadPrivateField<List<ScriptableObject>>(window, "_selectedObjects"), Is.EqualTo(new[] { fixtureAsset }));
            Assert.That(ReadPrivateField<bool>(window, "_isLoadingObjectsAsync"), Is.False);
        }

        [Test]
        public void ShouldRestoreSettingsToggleAndGateRebuiltControlsWhilePaused()
        {
            object previousInstance = ReadSharedInstance();
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
            cleanup.Defer(() => RestoreSharedInstance(previousInstance));
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            EditorSurfaceCapture.ShowPopup(window);
            window.CreateGUI();
            ActionButtonToggle toggle = window.rootVisualElement.Q<ActionButtonToggle>("settings-selection-toggle");
            Button folder = window.rootVisualElement.Q<Button>("settings-data-folder-button");
            VisualElement inputProfile = window.rootVisualElement.Q("settings-input-profile");
            bool stored = toggle.value;
            Assert.That(toggle.enabledSelf, Is.True);
            Assert.That(folder.enabledSelf, Is.True);
            Assert.That(inputProfile.enabledSelf, Is.True);
            SuspendForPlayMode(window);
            Assert.That(toggle.enabledSelf, Is.False);
            Assert.That(folder.enabledSelf, Is.False);
            Assert.That(inputProfile.enabledSelf, Is.False);
            // Deliver a stale change even though normal disabled controls suppress events.
            toggle.SetEnabled(true);
            Assert.DoesNotThrow(() => toggle.value = !stored);
            Assert.That(toggle.value, Is.EqualTo(stored));
            Assert.DoesNotThrow(() => InvokePrivate(window, "SelectDataFolderForPopover", new Label()));
            InvokePrivate(window, "BuildSettingsPopoverContent");
            toggle = window.rootVisualElement.Q<ActionButtonToggle>("settings-selection-toggle");
            folder = window.rootVisualElement.Q<Button>("settings-data-folder-button");
            inputProfile = window.rootVisualElement.Q("settings-input-profile");
            Assert.That(toggle.enabledSelf, Is.False);
            Assert.That(folder.enabledSelf, Is.False);
            Assert.That(inputProfile.enabledSelf, Is.False);
            Assert.That(toggle.value, Is.EqualTo(stored));
            ResumeFromPlayMode(window);
            Assert.That(toggle.enabledSelf, Is.True);
            Assert.That(folder.enabledSelf, Is.True);
            Assert.That(inputProfile.enabledSelf, Is.True);
        }

        [Test]
        public void ShouldShowPausedIndicatorAndDisableMutationButtonsWhileSuspendedForPlayMode()
        {
            object previousInstance = ReadSharedInstance();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
                cleanup.Defer(() => RestoreSharedInstance(previousInstance));

                DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));
                window.CreateGUI();

                Label pausedIndicator = window.rootVisualElement.Q<Label>("paused-indicator");
                Button createButton = window.rootVisualElement.Q<Button>("create-object-button");
                Button addTypeButton = window.rootVisualElement.Q<Button>("add-type-button");
                Assert.That(addTypeButton.text, Is.EqualTo("Add types"));
                Assert.That(window.rootVisualElement.Q<TextField>("global-search-field").label, Is.EqualTo("Search all assets"));
                Assert.That(window.rootVisualElement.Q<TextField>("type-search-field").value, Is.EqualTo("Filter types"));

                Assert.AreEqual(DisplayStyle.None, pausedIndicator.style.display.value);
                Assert.IsTrue(createButton.enabledSelf);
                Assert.IsTrue(addTypeButton.enabledSelf);

                SuspendForPlayMode(window);
                Assert.AreEqual(DisplayStyle.Flex, pausedIndicator.style.display.value);
                Assert.IsFalse(createButton.enabledSelf);
                Assert.IsFalse(addTypeButton.enabledSelf);

                ResumeFromPlayMode(window);
                Assert.AreEqual(DisplayStyle.None, pausedIndicator.style.display.value);
                Assert.IsTrue(createButton.enabledSelf);
                Assert.IsTrue(addTypeButton.enabledSelf);
            }
        }

        [Test]
        public void ShouldGatePreOpenedConfirmationWhileSuspendedForPlayMode()
        {
            object previousInstance = ReadSharedInstance();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
                cleanup.Defer(() => RestoreSharedInstance(previousInstance));

                DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));
                window.CreateGUI();

                int confirmCount = 0;
                InvokePrivate(window, "BuildAndOpenConfirmationPopover", "Test confirm", "Run", (Action)(() => confirmCount++), new VisualElement());

                VisualElement confirmPopover = ReadPrivateField<VisualElement>(window, "_confirmActionPopover");
                Button confirmButton = confirmPopover.Q<Button>(className: "popover-delete-button");
                Assert.That(confirmButton != null, "the confirm button must carry the action");
                Action confirmAction = (Action)confirmButton.userData;
                Assert.AreEqual(0, confirmCount);

                SuspendForPlayMode(window);
                confirmAction();
                Assert.AreEqual(0, confirmCount, "a confirmation popover that was already open when play started must not run while suspended");

                ResumeFromPlayMode(window);
                confirmAction();
                Assert.AreEqual(1, confirmCount, "the same confirmation after resume must run");
            }
        }

        [Test]
        public void ShouldGateArrowKeyTypeBrowsingWhileSuspendedForPlayMode()
        {
            string folder = CreateFixtureFolder();
            object previousInstance = ReadSharedInstance();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() =>
                {
                    AssetDatabase.DeleteAsset(folder);
                    AssetDatabase.Refresh();
                });
                cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
                cleanup.Defer(() => RestoreSharedInstance(previousInstance));

                DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));
                window.CreateGUI();

                /*
                    Two managed types so the arrow-key wrap genuinely moves the selection; the
                    regression is the visual highlight drifting while the suspended SelectType
                    call no-ops.
                */
                CreateFixtureAsset(folder, "First.asset");
                Assert.IsTrue((bool)InvokePrivate(window, "AddManagedTypes", new List<Type> { typeof(TestDataObject), typeof(SelectionPersistenceGuidData) }));
                InvokePrivate(window, "BuildNamespaceView");

                NamespaceController controller = ReadPrivateField<NamespaceController>(window, "_namespaceController");
                controller.SelectType(window, typeof(TestDataObject));
                Dictionary<Type, VisualElement> typeCache = ReadControllerField<Dictionary<Type, VisualElement>>(controller, "_namespaceCache");
                VisualElement selectedRow = typeCache[typeof(TestDataObject)];
                VisualElement unselectedRow = typeCache[typeof(SelectionPersistenceGuidData)];

                SuspendForPlayMode(window);
                controller.IncrementTypeSelection(window);
                Assert.AreEqual(typeof(TestDataObject), controller.SelectedType, "arrow-key browsing while suspended must not change the selection");
                Assert.IsTrue(selectedRow.ClassListContains(StyleConstants.SelectedClass), "arrow-key browsing while suspended must not strip the selected row's highlight");
                Assert.IsFalse(unselectedRow.ClassListContains(StyleConstants.SelectedClass), "arrow-key browsing while suspended must not highlight another row");

                ResumeFromPlayMode(window);
                controller.IncrementTypeSelection(window);
                Assert.AreEqual(typeof(SelectionPersistenceGuidData), controller.SelectedType, "arrow-key browsing after resume must move the selection");
            }
        }

        [Test]
        public void ShouldBuildDisabledInspectorContentWhileSuspendedForPlayMode()
        {
            object previousInstance = ReadSharedInstance();
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
                cleanup.Defer(() => RestoreSharedInstance(previousInstance));

                DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
                cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(window));
                window.CreateGUI();

                VisualElement inspectorContainer = ReadPrivateField<VisualElement>(window, "_inspectorContainer");
                Assert.IsTrue(inspectorContainer.enabledSelf);

                SuspendForPlayMode(window);
                InvokePrivate(window, "BuildInspectorView");
                Assert.IsFalse(inspectorContainer.enabledSelf, "inspector content built while suspended must stay disabled");

                ResumeFromPlayMode(window);
                InvokePrivate(window, "BuildInspectorView");
                Assert.IsTrue(inspectorContainer.enabledSelf, "inspector content built after resume must be enabled");
            }
        }
    }
}
