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
    using WallstopStudios.DataVisualizer.Editor.UI;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;

    public sealed class EditorInputBindingTests
    {
        [UnityTest]
        public IEnumerator ShouldKeepPrintableBindingsInTextFields()
        {
            using TestCleanupScope cleanup = new();
            LayoutTestWindow host = ScriptableObject.CreateInstance<LayoutTestWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(host));
            DataVisualizerInputSettings input = new();
            input.Navigation.NextTypeKey = KeyCode.N;
            input.nextFocus = new EditorInputBinding(KeyCode.N);
            SearchableThemeDropdown dropdown = new(Array.Empty<ThemeDropdownItem>(), null, null, getInput: () => input);
            host.rootVisualElement.Add(dropdown);
            host.position = new Rect(80, 80, 360, 240);
            EditorSurfaceCapture.ShowPopup(host);
            yield return null;
            EditorSurfaceCapture.SettleLayout(host);
            dropdown.SearchField.Focus();
            yield return null;
            Focusable textFocus = host.rootVisualElement.focusController.focusedElement;
            Assert.That(textFocus != null);
            VisualElement text = dropdown.SearchField.Q("unity-text-input");
            using (
                KeyDownEvent typed = KeyDownEvent.GetPooled(
                    new Event
                    {
                        type = EventType.KeyDown,
                        keyCode = KeyCode.N,
                        character = 'n',
                    }
                )
            )
            {
                text.SendEvent(typed);
            }
            yield return null;
            Assert.That(dropdown.SearchField.value, Is.EqualTo("n"));
            Assert.That(host.rootVisualElement.focusController.focusedElement, Is.SameAs(textFocus));
        }

        [UnityTest]
        public IEnumerator ShouldKeepNativeListChromeWithRemappedPrimaryPointer()
        {
            using TestCleanupScope cleanup = new();
            LayoutTestWindow host = ScriptableObject.CreateInstance<LayoutTestWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(host));
            DataVisualizerInputSettings input = new();
            input.Navigation.PointerButton = 1;
            ListView list = new()
            {
                itemsSource = new System.Collections.Generic.List<string> { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" },
                fixedItemHeight = 30,
                makeItem = () => new Label(),
                bindItem = (element, index) => ((Label)element).text = index.ToString(),
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                style = { height = 150 },
            };
            host.rootVisualElement.Add(list);
            host.position = new Rect(80, 80, 360, 240);
            EditorSurfaceCapture.ShowPopup(host);
            using ObjectListSelectionInput owner = new(list, host.rootVisualElement, () => input);
            yield return null;
            EditorSurfaceCapture.SettleLayout(host);
            VisualElement handle = list.Q(className: ListView.reorderableItemHandleUssClassName);
            Assert.That(handle != null);
            foreach (VisualElement chrome in new VisualElement[] { list.Q<ScrollView>().verticalScroller, handle })
            {
                int delivered = 0;
                chrome.RegisterCallback<PointerDownEvent>(_ => delivered++, TrickleDown.TrickleDown);
                foreach (EventType type in new[] { EventType.MouseDown, EventType.MouseUp })
                {
                    Event mouse = new()
                    {
                        type = type,
                        button = 0,
                        mousePosition = chrome.worldBound.center,
                        clickCount = 1,
                    };
                    if (type == EventType.MouseDown)
                    {
                        using PointerDownEvent down = PointerDownEvent.GetPooled(mouse);
                        chrome.SendEvent(down);
                    }
                    else
                    {
                        using PointerUpEvent up = PointerUpEvent.GetPooled(mouse);
                        chrome.SendEvent(up);
                    }
                    yield return null;
                }
                Assert.That(delivered, Is.EqualTo(1), "Native scrollbar/reorder chrome still receives the physical primary button.");
            }
        }

        [UnityTest]
        public IEnumerator ShouldRemapNativeButtonActivationAndSuppressOldSubmitKeys()
        {
            using TestCleanupScope cleanup = new();
            LayoutTestWindow host = ScriptableObject.CreateInstance<LayoutTestWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(host));
            DataVisualizerInputSettings input = new();
            input.Navigation.SelectKey = KeyCode.F6;
            input.Navigation.AlternateSelectKey = KeyCode.None;
            input.Navigation.ToggleKey = KeyCode.None;
            int clicks = 0;
            Button button = new(() => clicks++) { text = "Configured action" };
            host.rootVisualElement.Add(button);
            host.rootVisualElement.RegisterCallback<KeyDownEvent>(evt => input.HandleButton(evt), TrickleDown.TrickleDown);
            host.position = new Rect(80, 80, 360, 240);
            EditorSurfaceCapture.ShowPopup(host);
            yield return null;
            EditorSurfaceCapture.SettleLayout(host);
            button.Focus();
            using (KeyDownEvent configured = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.F6 }))
            {
                button.SendEvent(configured);
            }
            Assert.That(clicks, Is.EqualTo(1));
            using (KeyDownEvent old = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Return }))
            {
                button.SendEvent(old);
            }
            Assert.That(clicks, Is.EqualTo(1), "The old native submit must not also invoke the action.");
            NativeInputRouter focus = new();
            input.nextFocus = new EditorInputBinding(KeyCode.F4);
            input.previousFocus = new EditorInputBinding(KeyCode.F5);
            host.rootVisualElement.RegisterCallback<KeyDownEvent>(evt => focus.HandleFocusOrButton(evt, input), TrickleDown.TrickleDown);
            TextField next = new() { name = "next-focus-field" };
            host.rootVisualElement.Add(next);
            EditorSurfaceCapture.SettleLayout(host);
            button.Focus();
            using (KeyDownEvent configured = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.F4 }))
            {
                button.SendEvent(configured);
            }
            yield return null;
            VisualElement focused = host.rootVisualElement.focusController.focusedElement as VisualElement;
            Assert.That(focused == next || next.Contains(focused), "Configured focus navigation reaches the next control.");
            VisualElement textInput = next.Q("unity-text-input");
            Assert.That(textInput != null);
            using (KeyDownEvent old = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Tab }))
            {
                textInput.SendEvent(old);
            }
            Assert.That(host.rootVisualElement.focusController.focusedElement, Is.SameAs(focused), "An unbound native Tab does not move focus.");
            using (KeyDownEvent configured = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.F5 }))
            {
                textInput.SendEvent(configured);
            }
            yield return null;
            Assert.That(host.rootVisualElement.focusController.focusedElement, Is.SameAs(button));
        }

        [UnityTest]
        public IEnumerator ShouldRemapNativeListNavigationWithoutLeavingTheOldKeyActive()
        {
            using TestCleanupScope cleanup = new();
            LayoutTestWindow host = ScriptableObject.CreateInstance<LayoutTestWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(host));
            DataVisualizerInputSettings input = new();
            input.Navigation.NextTypeKey = KeyCode.N;
            ListView list = new()
            {
                itemsSource = new[] { "first", "second", "third" },
                fixedItemHeight = 30,
                makeItem = () => new Label { pickingMode = PickingMode.Position },
                bindItem = (element, index) => ((Label)element).text = index.ToString(),
                style = { height = 180 },
            };
            host.rootVisualElement.Add(list);
            host.position = new Rect(80, 80, 360, 240);
            EditorSurfaceCapture.ShowPopup(host);
            string route = string.Empty;
            host.rootVisualElement.RegisterCallback<KeyDownEvent>(evt => route += " root:" + evt.keyCode + ":" + list.selectedIndex, TrickleDown.TrickleDown);
            VisualElement recycledTarget = null;
            host.rootVisualElement.RegisterCallback<PointerDownEvent>(
                evt =>
                {
                    if (recycledTarget != null && evt.button == 0)
                    {
                        recycledTarget.userData = "recycled-row";
                    }
                },
                TrickleDown.TrickleDown
            );
            using ObjectListSelectionInput owner = new(list, host.rootVisualElement, () => input);
            list.RegisterCallback<KeyDownEvent>(evt => route += " list:" + evt.keyCode + ":" + list.selectedIndex, TrickleDown.TrickleDown);
            host.rootVisualElement.RegisterCallback<PointerDownEvent>(
                evt => route += " pointer:" + evt.button + ":" + evt.modifiers + ":" + evt.position + ":primary=" + evt.isPrimary + ":routing=" + owner.IsRouting,
                TrickleDown.TrickleDown
            );
            list.RegisterCallback<PointerDownEvent>(
                evt => route += " listPointer:" + evt.button + ":" + evt.modifiers + ":" + evt.localPosition + ":" + list.selectedIndex,
                TrickleDown.TrickleDown
            );
            yield return null;
            EditorSurfaceCapture.SettleLayout(host);
            list.SetSelection(0);
            list.Focus();
            using (KeyDownEvent configured = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.N }))
            {
                list.SendEvent(configured);
            }
            Assert.That(list.selectedIndex, Is.EqualTo(1));
            using (KeyDownEvent old = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.DownArrow }))
            {
                list.SendEvent(old);
            }
            Assert.That(list.selectedIndex, Is.EqualTo(1), route);
            Assert.That(owner.IsRouting, Is.False, "Dispatch restores the relay guard.");
            input.Navigation.PointerButton = 1;
            input.toggleSelection = new EditorInputBinding(KeyCode.Space, EventModifiers.Alt);
            list.selectionType = SelectionType.Multiple;
            list.SetSelection(0);
            VisualElement second = list.Query<Label>().ToList().Find(label => label.text == "1");
            Assert.That(second != null);
            Assert.That(second.worldBound.Overlaps(list.Q<ScrollView>().contentViewport.worldBound), Is.True, "Pointer targets must be visible in the hosted viewport.");
            foreach (EventType type in new[] { EventType.MouseDown, EventType.MouseUp })
            {
                Event mouse = new()
                {
                    type = type,
                    button = 1,
                    modifiers = EventModifiers.Alt,
                    clickCount = 1,
                    mousePosition = second.worldBound.center,
                };
                if (type == EventType.MouseDown)
                {
                    using PointerDownEvent down = PointerDownEvent.GetPooled(mouse);
                    second.SendEvent(down);
                }
                else
                {
                    using PointerUpEvent up = PointerUpEvent.GetPooled(mouse);
                    second.SendEvent(up);
                }
                yield return null;
            }
            Assert.That(list.selectedIndices, Is.EquivalentTo(new[] { 0, 1 }), "Configured primary/additive gestures reach native list selection. " + route);
            list.SetSelection(0);
            second.userData = "original-row";
            recycledTarget = second;
            foreach (EventType type in new[] { EventType.MouseDown, EventType.MouseUp })
            {
                Event mouse = new()
                {
                    type = type,
                    button = 1,
                    modifiers = EventModifiers.Alt,
                    clickCount = 1,
                    mousePosition = second.worldBound.center,
                };
                if (type == EventType.MouseDown)
                {
                    using PointerDownEvent down = PointerDownEvent.GetPooled(mouse);
                    second.SendEvent(down);
                }
                else
                {
                    using PointerUpEvent up = PointerUpEvent.GetPooled(mouse);
                    second.SendEvent(up);
                }
            }
            yield return null;
            Assert.That(list.selectedIndices, Is.EquivalentTo(new[] { 0 }), "Queued pointer input cannot select a recycled row identity.");
        }

        [TestCase(EventModifiers.Control, true)]
        [TestCase(EventModifiers.Command, true)]
        [TestCase(EventModifiers.Control | EventModifiers.Shift, false)]
        [TestCase(EventModifiers.Alt, false)]
        [TestCase(EventModifiers.None, false)]
        public void ShouldMatchExactActionModifiers(EventModifiers modifiers, bool matches)
        {
            EditorInputBinding binding = new(KeyCode.D, actionModifier: true);
            using KeyDownEvent evt = KeyDownEvent.GetPooled(
                new Event
                {
                    type = EventType.KeyDown,
                    keyCode = KeyCode.D,
                    modifiers = modifiers,
                }
            );
            Assert.That(binding.Matches(evt), Is.EqualTo(matches));
            binding.key = KeyCode.None;
            Assert.That(binding.Matches(evt), Is.False);
        }

        [Test]
        public void ShouldRoundTripCustomInputAndKeepDefaultsIndependent()
        {
            DataVisualizerInputSettings first = new();
            DataVisualizerInputSettings second = new();
            first.Navigation.NextTypeKey = KeyCode.N;
            first.clone = new EditorInputBinding(KeyCode.C, EventModifiers.Alt);
            string json = JsonUtility.ToJson(first);
            DataVisualizerInputSettings restored = JsonUtility.FromJson<DataVisualizerInputSettings>(json);
            Assert.That(restored.Navigation.NextTypeKey, Is.EqualTo(KeyCode.N));
            Assert.That(restored.clone.key, Is.EqualTo(KeyCode.C));
            Assert.That(restored.clone.modifiers, Is.EqualTo(EventModifiers.Alt));
            Assert.That(second.Navigation.NextTypeKey, Is.EqualTo(KeyCode.DownArrow));
            Assert.That(
                JsonUtility.FromJson<DataVisualizerInputSettings>("{}").rangeSelectionModifiers,
                Is.EqualTo(EventModifiers.Shift),
                "Missing serialized range bindings retain the field initializer."
            );
            first.rangeSelectionModifiers = EventModifiers.None;
            Assert.That(
                JsonUtility.FromJson<DataVisualizerInputSettings>(JsonUtility.ToJson(first)).rangeSelectionModifiers,
                Is.EqualTo(EventModifiers.None),
                "Explicit None disables range mapping."
            );
            restored.Navigation.SelectKey = KeyCode.None;
            restored.Navigation.AlternateSelectKey = KeyCode.None;
            restored.Navigation.ToggleKey = KeyCode.None;
            Assert.That(restored.Navigation.IsSelectionKey(KeyCode.None), Is.False, "Unbinding must not activate unknown key events.");
        }

        [Test]
        public void ShouldReadAssignedAndEditedProfilesWithoutRebuildingCatalogs()
        {
            using TestCleanupScope cleanup = new();
            FieldInfo instance = typeof(DataVisualizerWindow).GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = instance.GetValue(null);
            cleanup.Defer(() => instance.SetValue(null, previous));
            DataVisualizerWindow owner = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(owner));
            const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
            object settings = typeof(DataVisualizerWindow).GetProperty("Settings", members).GetValue(owner);
            FieldInfo profileField = settings.GetType().GetField("inputProfile");
            object previousProfile = profileField.GetValue(settings);
            cleanup.Defer(() => profileField.SetValue(settings, previousProfile));
            DataVisualizerInputProfile profile = ScriptableObject.CreateInstance<DataVisualizerInputProfile>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(profile));
            Dictionary<string, List<Type>> managed = new();
            Dictionary<string, int> order = new();
            VirtualizedTypeCatalogView view = new(owner, new NamespaceController(managed, order), managed, order);
            profileField.SetValue(settings, profile);
            profile.Bindings.Navigation.NextTypeKey = KeyCode.N;
            Assert.That(view.Input.NextTypeKey, Is.EqualTo(KeyCode.N));
            profile.Bindings.Navigation.NextTypeKey = KeyCode.J;
            Assert.That(view.Input.NextTypeKey, Is.EqualTo(KeyCode.J));
            profileField.SetValue(settings, null);
            Assert.That(view.Input.NextTypeKey, Is.EqualTo(KeyCode.DownArrow));
        }
    }
}
