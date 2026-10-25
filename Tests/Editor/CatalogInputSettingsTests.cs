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

    public sealed class CatalogInputSettingsTests
    {
        [Test]
        public void ShouldRejectDestroyedCatalogOwners()
        {
            using TestCleanupScope cleanup = new();
            FieldInfo instance = typeof(DataVisualizerWindow).GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = instance.GetValue(null);
            DataVisualizerWindow owner = null;
            cleanup.Defer(() =>
            {
                if (owner != null)
                {
                    UnityEngine.Object.DestroyImmediate(owner);
                }
                instance.SetValue(null, previous);
            });
            owner = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            UnityEngine.Object.DestroyImmediate(owner);
            Assert.That(owner == null);
            Dictionary<string, List<Type>> managed = new();
            Dictionary<string, int> order = new();
            NamespaceController controller = new(managed, order);
            Assert.Throws<ArgumentNullException>(() => new VirtualizedTypeCatalogView(owner, controller, managed, order));
        }

        [Test]
        public void ShouldDescribeCurrentInjectedCatalogKeys()
        {
            using TestCleanupScope cleanup = new();
            FieldInfo instance = typeof(DataVisualizerWindow).GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = instance.GetValue(null);
            cleanup.Defer(() => instance.SetValue(null, previous));
            DataVisualizerWindow owner = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            cleanup.Defer(() => UnityEngine.Object.DestroyImmediate(owner));
            Dictionary<string, List<Type>> managed = new();
            Dictionary<string, int> order = new();
            CatalogInputSettings input = new()
            {
                FirstRowKey = KeyCode.H,
                LastRowKey = KeyCode.J,
                NextTypeKey = KeyCode.N,
                PreviousPageKey = KeyCode.P,
                NextPageKey = KeyCode.Q,
            };
            VirtualizedTypeCatalogView view = new(owner, new NamespaceController(managed, order), managed, order, input);
            LayoutTestWindow host = ScriptableObject.CreateInstance<LayoutTestWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(host));
            host.rootVisualElement.Add(view);
            EditorSurfaceCapture.ShowPopup(host);
            using (TooltipEvent tooltip = TooltipEvent.GetPooled())
            {
                tooltip.target = view;
                view.SendEvent(tooltip);
                Assert.That(tooltip.tooltip, Does.Contain("H/J"));
                Assert.That(tooltip.tooltip, Does.Contain("P/Q"));
                Assert.That(tooltip.tooltip, Does.Contain("UpArrow/N"));
            }
            input.FirstRowKey = KeyCode.K;
            using (TooltipEvent tooltip = TooltipEvent.GetPooled())
            {
                tooltip.target = view;
                view.SendEvent(tooltip);
                Assert.That(tooltip.tooltip, Does.Contain("K/J"), "Tooltips follow mutable input settings.");
            }
            Button action = new() { tooltip = "Keep the action hint" };
            view.Add(action);
            using (TooltipEvent tooltip = TooltipEvent.GetPooled())
            {
                tooltip.target = action;
                action.SendEvent(tooltip);
                Assert.That(tooltip.tooltip, Is.EqualTo("Keep the action hint"));
            }
        }

        [Test]
        public void ShouldKeepCatalogEntryEqualityOrdinalAndExact()
        {
            CatalogEntry first = new("Data", typeof(CollisionA.OrderCollisionData));
            Assert.That(first.Equals(new CatalogEntry("Data", typeof(CollisionA.OrderCollisionData))), Is.True);
            Assert.That(first.Equals(new CatalogEntry("data", typeof(CollisionA.OrderCollisionData))), Is.False);
            Assert.That(first.Equals(new CatalogEntry("Data", typeof(CollisionB.OrderCollisionData))), Is.False);
            Assert.That(new CatalogEntry("Data", null).IsNamespace, Is.True);
            Assert.That(first.IsNamespace, Is.False);
            Assert.That(default(CatalogEntry).Equals(default(CatalogEntry)), Is.True);
        }

        [Test]
        public void ShouldUseConfiguredKeysAndLogicalDragDistance()
        {
            CatalogInputSettings settings = new() { SelectKey = KeyCode.E, DragThresholdPoints = 12 };
            Assert.That(settings.IsSelectionKey(KeyCode.E), Is.True);
            Assert.That(settings.IsSelectionKey(KeyCode.Return), Is.False);
            Assert.That(settings.HasPassedDragThreshold(Vector2.zero, new Vector2(12, 0)), Is.False);
            Assert.That(settings.HasPassedDragThreshold(Vector2.zero, new Vector2(12, 1)), Is.True);
            settings.DragThresholdPoints = 0;
            Assert.That(settings.HasPassedDragThreshold(Vector2.zero, Vector2.zero), Is.False);
            Assert.That(settings.HasPassedDragThreshold(Vector2.zero, Vector2.one), Is.True);
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void ShouldRejectInvalidDragDistanceWithoutChangingSettings(float value)
        {
            CatalogInputSettings settings = new();
            Assert.Throws<ArgumentOutOfRangeException>(() => settings.DragThresholdPoints = value);
            Assert.That(settings.DragThresholdPoints, Is.EqualTo(CatalogInputSettings.DefaultDragThresholdPoints));
        }
    }
}
