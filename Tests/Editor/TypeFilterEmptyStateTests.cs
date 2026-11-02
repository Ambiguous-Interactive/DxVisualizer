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

        private static void InvokePrivate(DataVisualizerWindow window, string methodName, params object[] arguments)
        {
            MethodInfo method = typeof(DataVisualizerWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(method != null, $"The {methodName} method must exist.");
            method.Invoke(window, arguments);
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
            Seeds the managed catalog directly so the check needs no assets. Each entry is one
            namespace and its type-row count: zero models a managed namespace with no type (or no
            managed namespace at all), one renders the compact catalog, and 129 switch it to the
            virtualized catalog.
        */
        private static void SeedCatalog(DataVisualizerWindow window, int[] namespaceSizes)
        {
            Dictionary<string, List<Type>> managed = ReadPrivateField<Dictionary<string, List<Type>>>(window, "_scriptableObjectTypes");
            managed.Clear();
            for (int namespaceIndex = 0; namespaceIndex < namespaceSizes.Length; namespaceIndex++)
            {
                List<Type> types = new(namespaceSizes[namespaceIndex]);
                for (int typeIndex = 0; typeIndex < namespaceSizes[namespaceIndex]; typeIndex++)
                {
                    types.Add(typeof(TestDataObject));
                }
                managed["Seed" + namespaceIndex] = types;
            }

            InvokePrivate(window, "BuildNamespaceView");
        }

        private static bool HasTypes(int[] namespaceSizes)
        {
            foreach (int size in namespaceSizes)
            {
                if (0 < size)
                {
                    return true;
                }
            }
            return false;
        }

        private static int TotalTypes(int[] namespaceSizes)
        {
            int total = 0;
            foreach (int size in namespaceSizes)
            {
                total += size;
            }
            return total;
        }

        /*
            Every managed namespace needs a group, including a namespace whose type list is empty:
            an empty group has no type row to compare, and a type-only rebuild check used to leave
            its header unrendered.
        */
        private static void AssertRenderedGroups(VisualElement root, int expected)
        {
            int rendered = 0;
            for (int index = 0; index < expected; index++)
            {
                if (root.Q("namespace-group-Seed" + index) != null)
                {
                    rendered++;
                }
            }
            Assert.That(rendered, Is.EqualTo(expected), "Every managed namespace renders one group header.");
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

        [TestCase(new int[] { })]
        [TestCase(new int[] { 0 })]
        [TestCase(new int[] { 0, 1 })]
        [TestCase(new int[] { 1 })]
        [TestCase(new int[] { 129 })]
        public void ShouldExplainAnEmptyFilterWhenTheCatalogHasNoVisibleType(int[] namespaceSizes)
        {
            using TestCleanupScope cleanup = new();
            FieldInfo instance = typeof(DataVisualizerWindow).GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = instance.GetValue(null);
            cleanup.Defer(() => instance.SetValue(null, previous));
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            cleanup.Defer(() => Object.DestroyImmediate(window));
            window.CreateGUI();
            SeedCatalog(window, namespaceSizes);

            VisualElement root = window.rootVisualElement;
            Assert.That(root.Q<TextField>("type-search-field") != null, "The catalog needs its filter field.");
            if (TotalTypes(namespaceSizes) <= 128)
            {
                AssertRenderedGroups(root, namespaceSizes.Length);
            }
            AssertEmptyState(root, false);

            ApplyTypeFilter(window, "NoSuchTypeName");
            AssertEmptyState(root, true);

            if (HasTypes(namespaceSizes))
            {
                ApplyTypeFilter(window, "TestDataObject");
                AssertEmptyState(root, false);
                ApplyTypeFilter(window, "NoSuchTypeName");
                AssertEmptyState(root, true);
            }

            ApplyTypeFilter(window, string.Empty);
            AssertEmptyState(root, false);
        }
    }
}
