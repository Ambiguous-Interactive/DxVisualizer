namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor;
    using WallstopStudios.DataVisualizer.Editor.UI;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;

    public sealed class EditorInputBindingTests
    {
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
