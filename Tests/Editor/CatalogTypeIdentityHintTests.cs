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
    using CollisionA = WallstopStudios.DataVisualizer.Tests.Editor.TypeIdentityCollision.First.Data;
    using CollisionB = WallstopStudios.DataVisualizer.Tests.Editor.TypeIdentityCollision.Second.Data;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;
    using Object = UnityEngine.Object;

    /*
        PR #476 gave the virtualized catalog exact type identity hints but left the static
        catalog (128 types or fewer, the common case) with an empty tooltip and only the
        automatic elision text. Both renderers now read one hint, so a viewer can always
        tell colliding short names and custom display names apart.
    */
    public sealed class CatalogTypeIdentityHintTests
    {
        private const string CustomTitle = "Friendly Custom Title";

        private static DataVisualizerWindow CreateOwner(TestCleanupScope cleanup)
        {
            FieldInfo instance = typeof(DataVisualizerWindow).GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = instance.GetValue(null);
            cleanup.Defer(() => instance.SetValue(null, previous));
            DataVisualizerWindow owner = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            cleanup.Defer(() => Object.DestroyImmediate(owner));
            return owner;
        }

        private static Dictionary<Type, string> BuildStaticHints(
            DataVisualizerWindow owner,
            Dictionary<string, List<Type>> managed,
            Dictionary<string, int> order,
            params Type[] types
        )
        {
            NamespaceController controller = new(managed, order);
            VisualElement catalog = null;
            controller.Build(owner, ref catalog);
            FieldInfo cacheField = typeof(NamespaceController).GetField("_namespaceCache", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(cacheField != null, "The static catalog must keep its type-row cache.");
            Dictionary<Type, VisualElement> cache = (Dictionary<Type, VisualElement>)cacheField.GetValue(controller);
            Dictionary<Type, string> hints = new();
            foreach (Type type in types)
            {
                Assert.That(cache.TryGetValue(type, out VisualElement row), Is.True, type.FullName + " must render a catalog row.");
                Label label = row.Q<Label>("type-item-label");
                Assert.That(label != null, type.FullName + " must render its type label.");
                Assert.That(label.displayTooltipWhenElided, Is.False, "The explicit identity hint must not be replaced by the elision tooltip.");
                hints[type] = label.tooltip;
            }
            return hints;
        }

        private static HashSet<string> BuildVirtualizedHints(DataVisualizerWindow owner, Dictionary<string, List<Type>> managed, Dictionary<string, int> order, string namespaceKey)
        {
            NamespaceController controller = new(managed, order);
            VirtualizedTypeCatalogView view = new(owner, controller, managed, order);
            Assert.That(view.List.itemsSource.Count, Is.Positive, "The virtualized catalog must render rows.");
            HashSet<string> typeHints = new(StringComparer.Ordinal);
            VisualElement slot = view.List.makeItem();
            for (int index = 0; index < view.List.itemsSource.Count; index++)
            {
                view.List.bindItem(slot, index);
                Label label = slot.Q<Label>("catalog-label");
                Assert.That(label != null, "A virtualized catalog row must render its label.");
                Assert.That(label.displayTooltipWhenElided, Is.False, "The explicit identity hint must not be replaced by the elision tooltip.");
                if (string.Equals(label.text, namespaceKey, StringComparison.Ordinal))
                {
                    Assert.That(label.tooltip, Is.EqualTo(namespaceKey), "A namespace header keeps its key hint.");
                }
                else
                {
                    typeHints.Add(label.tooltip);
                }
            }
            return typeHints;
        }

        [Test]
        public void ShouldExposeTheSameExactIdentityHintsInBothCatalogRenderersWhenShortNamesCollide()
        {
            using TestCleanupScope cleanup = new();
            DataVisualizerWindow owner = CreateOwner(cleanup);
            Type first = typeof(CollisionA.OrderCollisionData);
            Type second = typeof(CollisionB.OrderCollisionData);
            string namespaceKey = NamespaceController.GetNamespaceKey(first);
            Assert.That(NamespaceController.GetNamespaceKey(second), Is.EqualTo(namespaceKey), "The collision fixture must share one namespace key.");
            Dictionary<string, List<Type>> managed = new()
            {
                {
                    namespaceKey,
                    new List<Type> { first, second }
                },
            };
            Dictionary<string, int> order = new();

            Dictionary<Type, string> staticHints = BuildStaticHints(owner, managed, order, first, second);
            HashSet<string> virtualizedHints = BuildVirtualizedHints(owner, managed, order, namespaceKey);

            foreach (Type type in new[] { first, second })
            {
                Assert.That(staticHints[type], Is.EqualTo(type.FullName), "A static row must hint the exact namespace-qualified name.");
                Assert.That(virtualizedHints.Contains(staticHints[type]), Is.True, "Both renderers must agree on the identity hint.");
            }
            Assert.That(staticHints[first], Is.Not.EqualTo(staticHints[second]), "Colliding short names must stay distinguishable by tooltip.");
        }

        [Test]
        public void ShouldPrefixCustomDisplayNamesWithTheirFullTypeName()
        {
            using TestCleanupScope cleanup = new();
            DataVisualizerWindow owner = CreateOwner(cleanup);
            Type type = typeof(CustomTitledData);
            string namespaceKey = NamespaceController.GetNamespaceKey(type);
            Dictionary<string, List<Type>> managed = new()
            {
                {
                    namespaceKey,
                    new List<Type> { type }
                },
            };
            Dictionary<string, int> order = new();

            Dictionary<Type, string> hints = BuildStaticHints(owner, managed, order, type);

            Assert.That(hints[type], Is.EqualTo(CustomTitle + "\n" + type.FullName), "A custom display name must keep its exact type identity visible.");
        }

        [CustomDataVisualization(TypeName = CustomTitle)]
        private sealed class CustomTitledData : ScriptableObject { }
    }
}
