namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;
    using Object = UnityEngine.Object;

    public sealed class TypeFilterEmptyStateTests
    {
        private const string EmptyMessage = "No types match the current filter.";

        private static T ReadPrivateField<T>(DataVisualizerWindow window, string fieldName)
        {
            FieldInfo field = typeof(DataVisualizerWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(field != null, $"The {fieldName} field must exist.");
            return (T)field.GetValue(window);
        }

        private static object InvokePrivate(DataVisualizerWindow window, string methodName, params object[] arguments)
        {
            MethodInfo method = typeof(DataVisualizerWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(method != null, $"The {methodName} method must exist.");
            return method.Invoke(window, arguments);
        }

        /*
            An unshown window has no panel, so the filter field cannot dispatch its change event.
            Call the handler the field's callback runs to see the same catalog state a user sees.
        */
        private static void ApplyTypeFilter(DataVisualizerWindow window, string filter)
        {
            InvokePrivate(window, "PerformTypeSearch", filter);
        }

        /*
            Seeds the managed catalog directly so the check needs no assets. One namespace holds
            the repeated fixture type: one entry renders the compact catalog, 129 switch it to the
            virtualized catalog.
        */
        private static void SeedCatalog(DataVisualizerWindow window, int entriesInNamespace)
        {
            string namespaceKey = NamespaceController.GetNamespaceKey(typeof(TestDataObject));
            Dictionary<string, List<Type>> managed = ReadPrivateField<Dictionary<string, List<Type>>>(window, "_scriptableObjectTypes");
            managed.Clear();
            List<Type> types = new(entriesInNamespace);
            for (int index = 0; index < entriesInNamespace; index++)
            {
                types.Add(typeof(TestDataObject));
            }
            managed[namespaceKey] = types;
            InvokePrivate(window, "BuildNamespaceView");
        }

        private static void AssertEmptyState(VisualElement root, bool shown)
        {
            Label empty = root.Q<Label>("empty-type-list-label");
            VisualElement scroll = root.Q("namespace-scrollview");
            Assert.That(empty != null, "The catalog needs an empty-filter label.");
            Assert.That(scroll != null, "The catalog needs its type list container.");
            Assert.That(empty.text, Is.EqualTo(shown ? EmptyMessage : string.Empty));
            Assert.That(empty.style.display.value, Is.EqualTo(shown ? DisplayStyle.Flex : DisplayStyle.None));
            Assert.That(scroll.style.display.value, Is.EqualTo(shown ? DisplayStyle.None : DisplayStyle.Flex));
        }

        [TestCase(1)]
        [TestCase(129)]
        public void ShouldExplainAnEmptyFilterWhenEitherCatalogPathHidesEveryType(int entries)
        {
            using TestCleanupScope cleanup = new();
            FieldInfo instance = typeof(DataVisualizerWindow).GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = instance.GetValue(null);
            cleanup.Defer(() => instance.SetValue(null, previous));
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            cleanup.Defer(() => Object.DestroyImmediate(window));
            window.CreateGUI();
            SeedCatalog(window, entries);

            VisualElement root = window.rootVisualElement;
            Assert.That(root.Q<TextField>("type-search-field") != null, "The catalog needs its filter field.");
            AssertEmptyState(root, false);

            ApplyTypeFilter(window, "NoSuchTypeName");
            AssertEmptyState(root, true);

            ApplyTypeFilter(window, "TestDataObject");
            AssertEmptyState(root, false);

            ApplyTypeFilter(window, "NoSuchTypeName");
            AssertEmptyState(root, true);

            ApplyTypeFilter(window, string.Empty);
            AssertEmptyState(root, false);
        }
    }
}
