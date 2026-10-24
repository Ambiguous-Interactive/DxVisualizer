namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
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
    using CollisionA = WallstopStudios.DataVisualizer.Tests.Editor.TypeIdentityCollision.First.Data;
    using CollisionB = WallstopStudios.DataVisualizer.Tests.Editor.TypeIdentityCollision.Second.Data;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;
    using Object = UnityEngine.Object;

    public sealed class VirtualizedTypeCatalogTests
    {
        private static DataVisualizerUserState SavedState(PerfWindowStateScope state)
        {
            if (!state.Settings.persistStateInSettingsAsset)
            {
                return DataVisualizerUserState.FromJson(File.ReadAllText(state.FilePath));
            }
            DataVisualizerUserState saved = new();
            saved.HydrateFrom(state.Settings);
            return saved;
        }

        [UnityTest]
        public IEnumerator ShouldBoundRealizedCatalogRowsAcrossFilteringCollapseAndRecycling()
        {
            using TestCleanupScope cleanup = new();
            const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo instance = typeof(DataVisualizerWindow).GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = instance.GetValue(null);
            cleanup.Defer(() => instance.SetValue(null, previous));
            DataVisualizerWindow owner = null;
            PerfWindowStateScope state = null;
            cleanup.Defer(() =>
            {
                if (owner != null)
                {
                    Object.DestroyImmediate(owner);
                }
                state?.Dispose();
            });
            owner = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            DataVisualizerSettings settings = (DataVisualizerSettings)typeof(DataVisualizerWindow).GetProperty("Settings", members).GetValue(owner);
            DataVisualizerUserState user = (DataVisualizerUserState)typeof(DataVisualizerWindow).GetProperty("UserState", members).GetValue(owner);
            state = new PerfWindowStateScope(settings, user, "Temp/VirtualizedCatalog");
            typeof(DataVisualizerWindow).GetField("_settings", members).SetValue(owner, state.Settings);
            typeof(DataVisualizerWindow).GetField("_userState", members).SetValue(owner, state.UserState);
            typeof(DataVisualizerWindow).GetField("_userStateFilePath", members).SetValue(owner, state.FilePath);
            state.Settings.persistStateInSettingsAsset = false;
            state.UserState.namespaceCollapseStates.Clear();
            LayoutTestWindow host = ScriptableObject.CreateInstance<LayoutTestWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(host));
            host.position = new Rect(100, 100, 300, 500);
            host.rootVisualElement.AddToClassList("dataviz-root");
            host.rootVisualElement.styleSheets.Add(
                AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.wallstop-studios.data-visualizer/Editor/DataVisualizer/Styles/DataVisualizerStyles.uss")
            );
            EditorSurfaceCapture.ShowPopup(host);
            host.position = new Rect(100, 100, 300, 500);
            DataVisualizerThemeSelection themeSelection = new();
            cleanup.Defer(() => themeSelection.Apply(null, null));
            Type[][] datasets = CatalogLayoutScaleTests.CreateTypes(localized: true);
            foreach (Type[] dataset in datasets)
            {
                bool persistInSettings = ReferenceEquals(dataset, datasets[1]);
                state.Settings.persistStateInSettingsAsset = persistInSettings;
                Dictionary<string, List<Type>> managed = (Dictionary<string, List<Type>>)typeof(DataVisualizerWindow).GetField("_scriptableObjectTypes", members).GetValue(owner);
                Dictionary<string, int> order = (Dictionary<string, int>)typeof(DataVisualizerWindow).GetField("_namespaceOrder", members).GetValue(owner);
                managed.Clear();
                order.Clear();
                foreach (Type type in dataset)
                {
                    string key = NamespaceController.GetNamespaceKey(type);
                    if (!managed.TryGetValue(key, out List<Type> types))
                    {
                        types = new List<Type>();
                        managed.Add(key, types);
                        order.Add(key, order.Count);
                    }
                    types.Add(type);
                }
                NamespaceController controller = new(managed, order);
                VisualElement catalogRoot = null;
                controller.Build(owner, ref catalogRoot);
                VirtualizedTypeCatalogView view = controller.VirtualizedView;
                Assert.That(view != null);
                VisualElement focusedControl = null;
                Func<VisualElement> makeRow = view.List.makeItem;
                view.List.makeItem = () =>
                {
                    VisualElement row = makeRow();
                    row.RegisterCallback<FocusInEvent>(evt => focusedControl = evt.target as VisualElement);
                    return row;
                };
                ScrollView outer = new(ScrollViewMode.Vertical);
                outer.style.flexGrow = 1;
                outer.contentContainer.style.flexGrow = 1;
                outer.Add(catalogRoot);
                host.rootVisualElement.Add(outer);
                yield return null;
                EditorSurfaceCapture.SettleLayout(host);
                Assert.That(view.List.itemsSource.Count, Is.EqualTo(2000 + managed.Count));
                foreach (string themeName in new[] { "Dx", "Classic", "Nord", "Dracula", "Compact", "Minimal" })
                {
                    themeSelection.Apply(
                        host.rootVisualElement,
                        AssetDatabase.LoadAssetAtPath<DataVisualizerThemeSettings>(
                            "Packages/com.wallstop-studios.data-visualizer/Editor/DataVisualizer/Styles/" + themeName + ".asset"
                        )
                    );
                    yield return null;
                    EditorSurfaceCapture.SettleLayout(host);
                    foreach (VisualElement row in view.Query<VisualElement>(className: StyleConstants.VirtualCatalogRowClass).ToList())
                    {
                        Label title = row.Q<Label>("catalog-label");
                        Assert.That(title.worldBound.xMax, Is.LessThanOrEqualTo(row.worldBound.xMax), themeName);
                        Assert.That(title.resolvedStyle.fontSize, Is.Positive, themeName);
                    }
                }

                List<VisualElement> rowsBefore = view.Query<VisualElement>(className: StyleConstants.VirtualCatalogRowClass).ToList();
                controller.Build(owner, ref catalogRoot);
                CollectionAssert.AreEqual(
                    rowsBefore,
                    view.Query<VisualElement>(className: StyleConstants.VirtualCatalogRowClass).ToList(),
                    "Unchanged catalog refresh preserves rows"
                );
                string firstKey = NamespaceController.GetNamespaceKey(dataset[0]);
                List<Type> firstGroup = managed[firstKey];
                Type groupLast = firstGroup[firstGroup.Count - 1];
                Assert.That(
                    view.Drop(new CatalogEntry(firstKey, firstGroup[1]), new CatalogEntry(firstKey, firstGroup[0]), true),
                    Is.False,
                    "An unchanged adjacent drop is a no-op"
                );
                view.Drop(new CatalogEntry(firstKey, dataset[0]), new CatalogEntry(firstKey, groupLast), true);
                Assert.That(firstGroup[firstGroup.Count - 1], Is.EqualTo(dataset[0]));
                List<NamespaceTypeOrder> savedOrders = SavedState(state).typeOrders;
                Assert.That(savedOrders.Find(entry => entry.namespaceKey == firstKey).typeNames[firstGroup.Count - 1], Is.EqualTo(dataset[0].FullName));

                yield return null;
                List<VisualElement> gestureRows = view.Query<VisualElement>(className: StyleConstants.VirtualCatalogRowClass).ToList();
                Type dragged = firstGroup[0];
                VisualElement sourceRow = gestureRows.Find(row => row.userData is CatalogEntry entry && entry.Type == dragged);
                VisualElement targetRow = gestureRows.Find(row => row.userData is CatalogEntry entry && entry.Type == firstGroup[1]);
                Assert.That(sourceRow != null && targetRow != null);
                Vector2 destination = new(targetRow.worldBound.center.x, targetRow.worldBound.yMax - 2);
                using (
                    PointerDownEvent down = PointerDownEvent.GetPooled(
                        new Event
                        {
                            type = EventType.MouseDown,
                            button = 0,
                            mousePosition = sourceRow.worldBound.center,
                        }
                    )
                )
                {
                    down.target = sourceRow;
                    sourceRow.SendEvent(down);
                }
                Assert.That(sourceRow.HasPointerCapture(PointerId.mousePointerId), Is.True);
                using (
                    PointerMoveEvent move = PointerMoveEvent.GetPooled(
                        new Event
                        {
                            type = EventType.MouseDrag,
                            button = 0,
                            mousePosition = destination,
                        }
                    )
                )
                {
                    move.target = sourceRow;
                    sourceRow.SendEvent(move);
                }
                using (
                    PointerUpEvent up = PointerUpEvent.GetPooled(
                        new Event
                        {
                            type = EventType.MouseUp,
                            button = 0,
                            mousePosition = destination,
                        }
                    )
                )
                {
                    up.target = sourceRow;
                    sourceRow.SendEvent(up);
                }
                yield return null;
                Assert.That(firstGroup[1], Is.EqualTo(dragged), "Pointer drag commits the same canonical type order");
                Assert.That(controller.SelectedType == null, "Dragging does not select a type");

                Assert.That(view.Query<VisualElement>(className: StyleConstants.VirtualCatalogRowClass).ToList().Count, Is.InRange(1, 40));
                view.List.ScrollToItem(view.List.itemsSource.Count - 1);
                yield return null;
                EditorSurfaceCapture.SettleLayout(host);
                Assert.That(view.Query<VisualElement>(className: StyleConstants.VirtualCatalogRowClass).ToList().Count, Is.InRange(1, 40));
                Assert.That(view.List.Q<ScrollView>().scrollOffset.y, Is.Positive);
                VisualElement localizedRow = view.Query<VisualElement>(className: StyleConstants.VirtualCatalogRowClass)
                    .ToList()
                    .Find(row => row.userData is CatalogEntry entry && entry.Type == dataset[1997]);
                Assert.That(localizedRow != null);
                Assert.That(localizedRow.Q<Label>("catalog-label").text, Does.Contain("très long"));
                view.Filter("  OBJET très   LONG  ");
                yield return null;
                Assert.That(view.List.itemsSource.Count, Is.EqualTo(2), "Span terms preserve localized ordinal matching");
                view.Filter(null);
                Assert.That(localizedRow.Q<Label>("catalog-label").worldBound.xMax, Is.LessThanOrEqualTo(localizedRow.worldBound.xMax));

                Type last = dataset[dataset.Length - 1];
                view.Select(last);
                yield return null;
                Assert.That(view.ContainsType(last));
                Assert.That(view.Query<VisualElement>(className: StyleConstants.SelectedClass).ToList().Count, Is.Positive);
                view.Filter("MeasuredType1999Data");
                Assert.That(view.Filter("  MEASUREDTYPE1999DATA  "), Is.False, "Equivalent trimmed input keeps rows intact");
                yield return null;
                Assert.That(view.List.itemsSource.Count, Is.EqualTo(2));
                Assert.That(view.Adjacent(dataset[dataset.Length - 2], true), Is.EqualTo(last), "Navigation starts from hidden identity");
                view.Filter("missing type");
                yield return null;
                Assert.That(view.List.itemsSource.Count, Is.Zero);
                view.Filter(null);
                view.List.ScrollToItem(0);
                yield return null;
                Button collapse = view.Query<Button>("catalog-collapse").ToList().Find(button => button.style.display.value == DisplayStyle.Flex);

                Assert.That(collapse != null);
                Assert.That(collapse.focusable, Is.True, "Collapse is focusable");
                Assert.That(collapse.enabledInHierarchy, Is.True, "Collapse is enabled");
                Assert.That(collapse.worldBound.Overlaps(view.List.Q<ScrollView>().contentViewport.worldBound), Is.True, "Collapse is in the viewport");
                collapse.Focus();
                yield return null;
                Assert.That(focusedControl, Is.SameAs(collapse));
                using (KeyDownEvent left = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.LeftArrow }))
                {
                    left.target = collapse;
                    collapse.SendEvent(left);
                }
                yield return null;
                Assert.That(view.List.itemsSource.Count, Is.LessThan(2000 + managed.Count));
                for (int frame = 0; frame < 20 && (focusedControl is not Button || focusedControl.panel == null); frame++)
                {
                    yield return null;
                }
                Assert.That(focusedControl, Is.TypeOf<Button>(), "Rebuild restores the collapse control's focus");
                Assert.That(focusedControl.panel != null, "Focus belongs to a newly realized control");
                List<NamespaceCollapseState> collapsed = SavedState(state).namespaceCollapseStates;
                Assert.That(collapsed.Find(entry => entry.namespaceKey == firstKey).isCollapsed, Is.True);
                Assert.That(view.Adjacent(dataset[0], true) == null, "Collapsed groups do not navigate");
                VirtualizedTypeCatalogView reopened = new(owner, controller, managed, order);
                Assert.That(reopened.List.itemsSource.Count, Is.EqualTo(view.List.itemsSource.Count), "Reopen restores collapse in both stores");
                string lastKey = NamespaceController.GetNamespaceKey(dataset[dataset.Length - 1]);
                if (firstKey != lastKey)
                {
                    Type firstBefore = firstGroup[0];
                    view.Drop(new CatalogEntry(firstKey, firstBefore), new CatalogEntry(lastKey, dataset[dataset.Length - 1]), false);
                    Assert.That(firstGroup[0], Is.EqualTo(firstBefore), "Cross-namespace type drops do not move a type");
                    view.Drop(new CatalogEntry(firstKey, null), new CatalogEntry(lastKey, null), true);
                    Assert.That(order[firstKey], Is.EqualTo(order.Count - 1));
                    Assert.That(view.Drop(new CatalogEntry(firstKey, null), new CatalogEntry(lastKey, null), true), Is.False, "Repeating a namespace drop is a no-op");
                    Assert.That(SavedState(state).namespaceOrder[order.Count - 1], Is.EqualTo(firstKey));
                }
                Type firstCollision = typeof(CollisionA.OrderCollisionData);
                Type secondCollision = typeof(CollisionB.OrderCollisionData);
                foreach (Type type in new[] { firstCollision, secondCollision })
                {
                    string key = NamespaceController.GetNamespaceKey(type);
                    if (!managed.TryGetValue(key, out List<Type> collisions))
                    {
                        collisions = new List<Type>();
                        managed.Add(key, collisions);
                        order.Add(key, order.Count);
                    }
                    collisions.Add(type);
                }
                typeof(DataVisualizerWindow).GetField("_namespaceController", members).SetValue(owner, controller);
                controller.Build(owner, ref catalogRoot);
                view.Input.NextTypeKey = KeyCode.N;
                controller.SelectType(owner, firstCollision);
                yield return null;
                Assert.That(controller.SelectedType, Is.EqualTo(firstCollision));
                VisualElement collisionRow = view.Query<VisualElement>(className: StyleConstants.VirtualCatalogRowClass)
                    .ToList()
                    .Find(row => row.userData is CatalogEntry entry && entry.Type == firstCollision);
                Assert.That(collisionRow != null);
                using (KeyDownEvent down = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.N }))
                {
                    down.target = collisionRow;
                    collisionRow.SendEvent(down);
                }
                yield return null;
                Assert.That(controller.SelectedType, Is.EqualTo(secondCollision), "Equal short names preserve exact selection");
                view.Filter("OrderCollisionData");
                yield return null;
                Assert.That(view.List.itemsSource.Count, Is.EqualTo(3));
                Assert.That(controller.SelectedType, Is.EqualTo(secondCollision), "Filtering preserves type selection");
                Assert.That(view.Drop(default, default, false), Is.False);
                managed[NamespaceController.GetNamespaceKey(secondCollision)].Remove(secondCollision);
                controller.Build(owner, ref catalogRoot);
                Assert.That(controller.SelectedType == null, "Removed types cannot remain selected");
                managed.Clear();
                order.Clear();
                string smallKey = NamespaceController.GetNamespaceKey(firstCollision);
                managed[smallKey] = new List<Type> { firstCollision };
                order[smallKey] = 0;
                controller.Build(owner, ref catalogRoot);
                Assert.That(controller.VirtualizedView == null, "Small catalogs keep the existing compact view");
                Assert.That(catalogRoot.Query<VisualElement>(className: StyleConstants.TypeItemClass).ToList().Count, Is.EqualTo(1));
                outer.RemoveFromHierarchy();
                state.UserState.namespaceCollapseStates.Clear();
                EditorJsonUtility.FromJsonOverwrite("{\"namespaceCollapseStates\":[]}", state.Settings);
            }
        }
    }
}
