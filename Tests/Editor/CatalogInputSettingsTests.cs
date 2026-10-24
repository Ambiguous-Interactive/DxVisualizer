namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.UI;
    using CollisionA = WallstopStudios.DataVisualizer.Tests.Editor.TypeIdentityCollision.First.Data;
    using CollisionB = WallstopStudios.DataVisualizer.Tests.Editor.TypeIdentityCollision.Second.Data;

    public sealed class CatalogInputSettingsTests
    {
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
