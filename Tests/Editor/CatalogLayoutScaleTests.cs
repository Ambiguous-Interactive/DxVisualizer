namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Reflection;
    using System.Reflection.Emit;
    using NUnit.Framework;
    using Unity.Profiling;
    using Unity.Profiling.LowLevel.Unsafe;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using WallstopStudios.DataVisualizer.Editor.Styles;
    using WallstopStudios.DataVisualizer.Editor.UI;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;
    using Object = UnityEngine.Object;

    public sealed class CatalogLayoutScaleTests
    {
        internal static Type[][] CreateTypes(bool localized = false)
        {
            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("CatalogLayoutScale" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
            ModuleBuilder module = assembly.DefineDynamicModule("CatalogLayoutScale");
            Type[][] types = { new Type[2000], new Type[2000] };
            for (int distribution = 0; distribution < types.Length; distribution++)
            {
                for (int i = 0; i < types[distribution].Length; i++)
                {
                    string group = distribution == 0 ? "SingleGroup" : "Group" + (i / 10).ToString("D4");
                    if (localized && (distribution == 0 || 1990 <= i))
                    {
                        group = "Un très long espace de noms pour vérifier la lisibilité";
                    }
                    string name = "CatalogLayoutScale." + (localized ? "Distribution" + distribution + "." : string.Empty) + group + ".MeasuredType" + i.ToString("D4") + "Data";
                    TypeBuilder builder = module.DefineType(name, TypeAttributes.Public, typeof(object));
                    if (localized && i == 1997)
                    {
                        builder.SetCustomAttribute(
                            new CustomAttributeBuilder(
                                typeof(CustomDataVisualizationAttribute).GetConstructor(Type.EmptyTypes),
                                Array.Empty<object>(),
                                new[] { typeof(CustomDataVisualizationAttribute).GetProperty(nameof(CustomDataVisualizationAttribute.TypeName)) },
                                new object[] { "Un objet avec un nom très long pour vérifier les contrôles du catalogue" }
                            )
                        );
                    }
                    types[distribution][i] = builder.CreateType();
                }
            }
            return types;
        }

        private static ProfilerRecorder CreateFrameAllocationRecorderOrDefault()
        {
            List<ProfilerRecorderHandle> handles = new();
            ProfilerRecorderHandle.GetAvailable(handles);
            foreach (ProfilerRecorderHandle handle in handles)
            {
                ProfilerRecorderDescription description = ProfilerRecorderHandle.GetDescription(handle);
                if (string.Equals(description.Name, "GC Allocated In Frame", StringComparison.Ordinal))
                {
                    return ProfilerRecorder.StartNew(description.Category, description.Name, 1);
                }
            }
            return default;
        }

        [UnityTest, Explicit("Synthetic type-catalog layout; creates no Unity assets")]
        [Timeout(650000)]
        public IEnumerator ShouldMeasureLargeCatalogConstructionAndScrolling()
        {
            BenchmarkCodeIdentity production = new(
                typeof(NamespaceController).Assembly,
                "Packages/com.wallstop-studios.data-visualizer/Editor/DataVisualizer/NamespaceController.cs"
            );
            BenchmarkCodeIdentity driver = new(typeof(CatalogLayoutScaleTests).Assembly, "Packages/com.wallstop-studios.data-visualizer/Tests/Editor/CatalogLayoutScaleTests.cs");
            BenchmarkCodeIdentity renderer = new(
                typeof(VirtualizedTypeCatalogView).Assembly,
                "Packages/com.wallstop-studios.data-visualizer/Editor/DataVisualizer/UI/VirtualizedTypeCatalogView.cs"
            );
            production.Verify();
            driver.Verify();
            renderer.Verify();
            CatalogReport report = new()
            {
                unityVersion = Application.unityVersion,
                operatingSystem = SystemInfo.operatingSystem,
                processor = SystemInfo.processorType,
                productionSource = production.SourceSha256,
                productionModule = production.LoadedModuleId,
                driverSource = driver.SourceSha256,
                driverModule = driver.LoadedModuleId,
                rendererSource = renderer.SourceSha256,
                rendererModule = renderer.LoadedModuleId,
                rendererAssembly = renderer.AssemblySha256,
                editorIsProSkin = EditorGUIUtility.isProSkin,
                pixelsPerPoint = EditorGUIUtility.pixelsPerPoint,
            };
            Stopwatch budget = Stopwatch.StartNew();
            Directory.CreateDirectory("Temp");
            using TestCleanupScope reportCleanup = new();
            reportCleanup.Defer(() =>
            {
                report.elapsedMilliseconds = budget.Elapsed.TotalMilliseconds;
                File.WriteAllText("Temp/catalog-layout-scale.json", JsonUtility.ToJson(report));
            });
            using TestCleanupScope cleanup = new();
            DocsImageCapture.WindowPreferenceSnapshot preferences = DocsImageCapture.WindowPreferenceSnapshot.Capture();
            cleanup.Defer(preferences.Restore);
            int hostsBefore = EditorSurfaceCapture.CountNativeWindowHosts();
            int windowsBefore = Resources.FindObjectsOfTypeAll<EditorWindow>().Length;
            FieldInfo instance = typeof(DataVisualizerWindow).GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = instance.GetValue(null);
            DataVisualizerWindow owner = null;
            PerfWindowStateScope state = null;
            cleanup.Defer(() =>
            {
                if (owner != null)
                {
                    Object.DestroyImmediate(owner);
                }
                state?.Dispose();
                instance.SetValue(null, previous);
            });
            owner = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
            DataVisualizerSettings settings = (DataVisualizerSettings)typeof(DataVisualizerWindow).GetProperty("Settings", members).GetValue(owner);
            DataVisualizerUserState userState = (DataVisualizerUserState)typeof(DataVisualizerWindow).GetProperty("UserState", members).GetValue(owner);
            state = new PerfWindowStateScope(settings, userState, "Temp/CatalogLayoutScale");
            typeof(DataVisualizerWindow).GetField("_settings", members).SetValue(owner, state.Settings);
            typeof(DataVisualizerWindow).GetField("_userState", members).SetValue(owner, state.UserState);
            typeof(DataVisualizerWindow).GetField("_userStateFilePath", members).SetValue(owner, state.FilePath);
            state.UserState.namespaceCollapseStates.Clear();
            LayoutTestWindow host = ScriptableObject.CreateInstance<LayoutTestWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(host));
            host.position = new Rect(40, 40, 360, 720);
            VisualElement root = host.rootVisualElement;
            root.AddToClassList("dataviz-root");
            root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.wallstop-studios.data-visualizer/Editor/DataVisualizer/Styles/DataVisualizerStyles.uss"));
            DataVisualizerThemeSelection theme = new();
            cleanup.Defer(() => theme.Apply(null, null));
            theme.Apply(root, DataVisualizerThemeSelection.ResolveDefault());
            ScrollView scroll = new(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.contentContainer.style.flexGrow = 1;
            root.Add(scroll);
            EditorSurfaceCapture.ShowPopup(host);
            host.position = new Rect(40, 40, 360, 720);
            using ProfilerRecorder frameAllocations = CreateFrameAllocationRecorderOrDefault();
            Type[][] types = CreateTypes();
            long initialHeap = GC.GetTotalMemory(true);
            long allocationControlStart = GC.GetAllocatedBytesForCurrentThread();
            byte[] allocationControl = new byte[4096];
            report.allocationControlObservedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationControlStart;
            report.perThreadAllocationCounterAvailable = 4096 <= report.allocationControlObservedBytes;
            yield return null;
            GC.KeepAlive(allocationControl);
            report.frameAllocationRecorderAvailable = frameAllocations.Valid;

            foreach (int count in new[] { 100, 1000, 2000 })
            {
                for (int distribution = 0; distribution < types.Length; distribution++)
                {
                    Dictionary<string, List<Type>> managed = new(StringComparer.Ordinal);
                    Dictionary<string, int> ranks = new(StringComparer.Ordinal);
                    for (int i = 0; i < count; i++)
                    {
                        Type type = types[distribution][i];
                        string key = NamespaceController.GetNamespaceKey(type);
                        if (!managed.TryGetValue(key, out List<Type> group))
                        {
                            group = new List<Type>();
                            managed.Add(key, group);
                            ranks.Add(key, -ranks.Count);
                        }
                        group.Add(type);
                    }
                    for (int repetition = -5; repetition < 30; repetition++)
                    {
                        Assert.That(budget.Elapsed.TotalMinutes, Is.LessThan(10), $"Ten-minute catalog budget: {count}/{distribution}/{repetition}");
                        scroll.Clear();
                        scroll.scrollOffset = Vector2.zero;
                        VisualElement catalogRoot = null;
                        long allocated = GC.GetAllocatedBytesForCurrentThread();
                        long constructionStarted = Stopwatch.GetTimestamp();
                        NamespaceController controller = new(managed, ranks);
                        controller.Build(owner, ref catalogRoot);
                        double constructionMilliseconds = (Stopwatch.GetTimestamp() - constructionStarted) * 1000.0 / Stopwatch.Frequency;
                        allocated = report.perThreadAllocationCounterAvailable ? GC.GetAllocatedBytesForCurrentThread() - allocated : -1;
                        scroll.Add(catalogRoot);
                        yield return null;
                        long editorFrameAllocatedBytes = frameAllocations.Valid && 0 < frameAllocations.Count ? frameAllocations.LastValue : -1;
                        EditorSurfaceCapture.SettleLayout(host);
                        Assert.That(root.worldBound.width, Is.EqualTo(360).Within(1), "Measure the actual popup width");
                        Assert.That(root.worldBound.height, Is.EqualTo(720).Within(1), "Measure the actual popup height");
                        int elements = catalogRoot.Query<VisualElement>().ToList().Count;
                        List<VisualElement> rows = catalogRoot.Query<VisualElement>(className: StyleConstants.TypeItemClass).ToList();
                        bool virtualized = controller.VirtualizedView != null;
                        if (virtualized)
                        {
                            Assert.That(controller.VirtualizedView.List.itemsSource.Count, Is.EqualTo(count + managed.Count));
                            Assert.That(rows.Count, Is.InRange(1, 40));
                        }
                        else
                        {
                            Assert.That(rows.Count, Is.EqualTo(count));
                        }
                        ScrollView activeScroll = virtualized ? controller.VirtualizedView.List.Q<ScrollView>() : scroll;
                        Action<VisualElement, int> scrollBinding = virtualized ? controller.VirtualizedView.List.bindItem : null;
                        int scrollBoundRows = 0;
                        long scrollBindingTicks = 0;
                        if (virtualized)
                        {
                            controller.VirtualizedView.List.bindItem = (row, index) =>
                            {
                                long started = Stopwatch.GetTimestamp();
                                scrollBinding(row, index);
                                scrollBindingTicks += Stopwatch.GetTimestamp() - started;
                                scrollBoundRows++;
                            };
                            yield return null;
                            EditorSurfaceCapture.SettleLayout(host);
                            scrollBoundRows = 0;
                            scrollBindingTicks = 0;
                        }
                        Stopwatch scrolling = Stopwatch.StartNew();
                        activeScroll.scrollOffset = new Vector2(0, activeScroll.verticalScroller.highValue);
                        double scrollCallMilliseconds = scrolling.Elapsed.TotalMilliseconds;
                        int visible = 0;
                        double realizationDeadline = EditorApplication.timeSinceStartup + 5;
                        do
                        {
                            yield return null;
                            EditorSurfaceCapture.SettleLayout(host);
                            rows = catalogRoot.Query<VisualElement>(className: StyleConstants.TypeItemClass).ToList();
                            visible = 0;
                            foreach (VisualElement row in rows)
                            {
                                if (row.worldBound.Overlaps(activeScroll.contentViewport.worldBound))
                                {
                                    visible++;
                                }
                            }
                        } while (visible == 0 && EditorApplication.timeSinceStartup < realizationDeadline);
                        double scrollMilliseconds = scrolling.Elapsed.TotalMilliseconds;
                        double scrollBindingMilliseconds = virtualized ? scrollBindingTicks * 1000.0 / Stopwatch.Frequency : -1;
                        int measuredScrollBoundRows = virtualized ? scrollBoundRows : -1;
                        if (virtualized)
                        {
                            Assert.That(measuredScrollBoundRows, Is.Positive, "Every virtualized scroll must bind new identities");
                            controller.VirtualizedView.List.bindItem = scrollBinding;
                        }
                        Assert.That(activeScroll.scrollOffset.y, Is.Positive, "Every scroll observation must move the viewport");
                        Assert.That(visible, Is.Positive);
                        double selectionMilliseconds = -1;
                        double observedSelectionMilliseconds = -1;
                        int selectionCreatedRows = -1;
                        int selectionBoundRows = -1;
                        if (virtualized)
                        {
                            VirtualizedTypeCatalogView view = controller.VirtualizedView;
                            view.Select(types[distribution][count - 2]);
                            yield return null;
                            EditorSurfaceCapture.SettleLayout(host);
                            Func<VisualElement> makeRow = view.List.makeItem;
                            Action<VisualElement, int> bindRow = view.List.bindItem;
                            int createdRows = 0;
                            int boundRows = 0;
                            view.List.makeItem = () =>
                            {
                                createdRows++;
                                return makeRow();
                            };
                            view.List.bindItem = (row, index) =>
                            {
                                boundRows++;
                                bindRow(row, index);
                            };
                            yield return null;
                            EditorSurfaceCapture.SettleLayout(host);
                            createdRows = 0;
                            boundRows = 0;
                            Stopwatch selectionClock = Stopwatch.StartNew();
                            view.Select(types[distribution][count - 1]);
                            selectionMilliseconds = selectionClock.Elapsed.TotalMilliseconds;
                            yield return null;
                            EditorSurfaceCapture.SettleLayout(host);
                            observedSelectionMilliseconds = selectionClock.Elapsed.TotalMilliseconds;
                            selectionCreatedRows = createdRows;
                            selectionBoundRows = boundRows;
                            view.List.makeItem = makeRow;
                            view.List.bindItem = bindRow;
                        }
                        CatalogSample sample = new()
                        {
                            types = count,
                            namespaces = managed.Count,
                            distribution = distribution == 0 ? "one-large-namespace" : "ten-types-per-namespace",
                            repetition = repetition,
                            constructionMilliseconds = constructionMilliseconds,
                            constructionAllocatedBytes = allocated,
                            editorFrameAllocatedBytesAfterConstruction = editorFrameAllocatedBytes,
                            observedScrollMilliseconds = scrollMilliseconds,
                            scrollCallMilliseconds = scrollCallMilliseconds,
                            scrollBindingMilliseconds = scrollBindingMilliseconds,
                            scrollBoundRows = measuredScrollBoundRows,
                            selectionMilliseconds = selectionMilliseconds,
                            observedSelectionMilliseconds = observedSelectionMilliseconds,
                            selectionCreatedRows = selectionCreatedRows,
                            selectionBoundRows = selectionBoundRows,
                            scrollOffset = activeScroll.scrollOffset.y,
                            virtualized = virtualized,
                            viewportWidth = activeScroll.contentViewport.worldBound.width,
                            viewportHeight = activeScroll.contentViewport.worldBound.height,
                            realizedCatalogRows = catalogRoot.Query<VisualElement>(className: StyleConstants.VirtualCatalogRowClass).ToList().Count,
                            realizedElementsAfterScroll = catalogRoot.Query<VisualElement>().ToList().Count,
                            realizedElements = elements,
                            realizedTypeRows = rows.Count,
                            viewportTypeRows = visible,
                            managedBytes = GC.GetTotalMemory(false),
                            unityAllocatedBytes = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(),
                        };
                        if (virtualized)
                        {
                            VirtualizedTypeCatalogView view = controller.VirtualizedView;
                            Stopwatch interaction = Stopwatch.StartNew();
                            bool filtered = view.Filter(types[distribution][count - 1].Name);
                            sample.filterCallMilliseconds = interaction.Elapsed.TotalMilliseconds;
                            Assert.That(filtered);
                            yield return null;
                            EditorSurfaceCapture.SettleLayout(host);
                            sample.filterObservedMilliseconds = interaction.Elapsed.TotalMilliseconds;
                            sample.filteredLogicalRows = view.List.itemsSource.Count;
                            Assert.That(sample.filteredLogicalRows, Is.EqualTo(2));
                            interaction.Restart();
                            bool cleared = view.Filter(null);
                            sample.clearFilterCallMilliseconds = interaction.Elapsed.TotalMilliseconds;
                            Assert.That(cleared);
                            yield return null;
                            EditorSurfaceCapture.SettleLayout(host);
                            sample.clearFilterObservedMilliseconds = interaction.Elapsed.TotalMilliseconds;
                            Assert.That(view.List.itemsSource.Count, Is.EqualTo(count + managed.Count));
                            view.List.ScrollToItem(0);
                            yield return null;
                            EditorSurfaceCapture.SettleLayout(host);
                            VisualElement header = view.Query<VisualElement>(className: StyleConstants.NamespaceHeaderClass).First();
                            Assert.That(header != null);
                            Assert.That(header.userData, Is.EqualTo(view.List.itemsSource[0]));
                            using (KeyDownEvent collapse = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = view.Input.CollapseKey }))
                            {
                                collapse.target = header;
                                interaction.Restart();
                                header.SendEvent(collapse);
                                sample.collapseCallMilliseconds = interaction.Elapsed.TotalMilliseconds;
                            }
                            yield return null;
                            EditorSurfaceCapture.SettleLayout(host);
                            sample.collapseObservedMilliseconds = interaction.Elapsed.TotalMilliseconds;
                            sample.collapsedLogicalRows = view.List.itemsSource.Count;
                            int removed = distribution == 0 ? count : 10;
                            Assert.That(sample.collapsedLogicalRows, Is.EqualTo(count + managed.Count - removed));
                            header = view.Query<VisualElement>(className: StyleConstants.NamespaceHeaderClass).First();
                            using (KeyDownEvent expand = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = view.Input.ExpandKey }))
                            {
                                expand.target = header;
                                interaction.Restart();
                                header.SendEvent(expand);
                                sample.expandCallMilliseconds = interaction.Elapsed.TotalMilliseconds;
                            }
                            yield return null;
                            EditorSurfaceCapture.SettleLayout(host);
                            sample.expandObservedMilliseconds = interaction.Elapsed.TotalMilliseconds;
                            Assert.That(view.List.itemsSource.Count, Is.EqualTo(count + managed.Count));
                        }
                        if (0 <= repetition)
                        {
                            report.samples.Add(sample);
                        }
                    }
                    if (count == 2000 && distribution == 1)
                    {
                        foreach (string themeName in new[] { "Dx", "Classic", "Nord", "Dracula", "Compact", "Minimal" })
                        {
                            DataVisualizerThemeSettings selection = AssetDatabase.LoadAssetAtPath<DataVisualizerThemeSettings>(
                                "Packages/com.wallstop-studios.data-visualizer/Editor/DataVisualizer/Styles/" + themeName + ".asset"
                            );
                            Assert.That(selection != null);
                            theme.Apply(root, selection);
                            yield return null;
                            EditorSurfaceCapture.Capture(host, "Temp/catalog-virtualized-" + themeName + ".png");
                        }
                    }
                    scroll.Clear();
                    Assert.That(
                        GC.GetTotalMemory(true) - initialHeap,
                        Is.LessThan(512L * 1024 * 1024),
                        "Stop after a condition if additional retained managed heap reaches 512 MiB"
                    );
                }
            }
            cleanup.Dispose();
            Assert.That(EditorSurfaceCapture.CountNativeWindowHosts(), Is.EqualTo(hostsBefore));
            Assert.That(Resources.FindObjectsOfTypeAll<EditorWindow>().Length, Is.EqualTo(windowsBefore));
            production.Verify();
            driver.Verify();
            renderer.Verify();
            report.nativeCleanupVerified = true;
            report.complete = true;
            report.failure = string.Empty;
        }

        [Serializable]
        private sealed class CatalogReport
        {
            public bool complete;
            public bool syntheticTypes = true;
            public string fixtureKind = "CLR classes; not Unity asset types";
            public string failure = "Experiment did not complete; see NUnit results.";
            public int generatedTypes = 4000;
            public int warmupsPerCondition = 5;
            public int samplesPerCondition = 30;
            public int timeoutMinutes = 10;
            public bool nativeCleanupVerified;
            public bool perThreadAllocationCounterAvailable;
            public long allocationControlObservedBytes;
            public bool frameAllocationRecorderAvailable;
            public string unityVersion;
            public string operatingSystem;
            public string processor;
            public string productionSource;
            public string productionModule;
            public string driverSource;
            public string driverModule;
            public string rendererSource;
            public string rendererModule;
            public string rendererAssembly;
            public bool editorIsProSkin;
            public float pixelsPerPoint;
            public double elapsedMilliseconds;
            public List<CatalogSample> samples = new();
        }

        [Serializable]
        private sealed class CatalogSample
        {
            public int types;
            public int namespaces;
            public string distribution;
            public int repetition;
            public double filterCallMilliseconds = -1;
            public double filterObservedMilliseconds = -1;
            public double clearFilterCallMilliseconds = -1;
            public double clearFilterObservedMilliseconds = -1;
            public double collapseCallMilliseconds = -1;
            public double collapseObservedMilliseconds = -1;
            public double expandCallMilliseconds = -1;
            public double expandObservedMilliseconds = -1;
            public int filteredLogicalRows = -1;
            public int collapsedLogicalRows = -1;
            public double constructionMilliseconds;
            public long constructionAllocatedBytes;
            public long editorFrameAllocatedBytesAfterConstruction;
            public double observedScrollMilliseconds;
            public double scrollCallMilliseconds;
            public double scrollBindingMilliseconds;
            public int scrollBoundRows;
            public double selectionMilliseconds;
            public double observedSelectionMilliseconds;
            public int selectionCreatedRows;
            public int selectionBoundRows;
            public float scrollOffset;
            public int realizedElements;
            public bool virtualized;
            public float viewportWidth;
            public float viewportHeight;
            public int realizedCatalogRows;
            public int realizedElementsAfterScroll;
            public int realizedTypeRows;
            public int viewportTypeRows;
            public long managedBytes;
            public long unityAllocatedBytes;
        }
    }
}
