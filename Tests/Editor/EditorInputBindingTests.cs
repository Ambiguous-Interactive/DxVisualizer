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
                makeItem = () => new Label(),
                bindItem = (element, index) => ((Label)element).text = index.ToString(),
                style = { height = 180 },
            };
            host.rootVisualElement.Add(list);
            host.position = new Rect(80, 80, 360, 240);
            EditorSurfaceCapture.ShowPopup(host);
            string route = string.Empty;
            host.rootVisualElement.RegisterCallback<KeyDownEvent>(evt => route += " root:" + evt.keyCode + ":" + list.selectedIndex, TrickleDown.TrickleDown);
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
