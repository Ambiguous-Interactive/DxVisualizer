namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System.Reflection;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;
    using Object = UnityEngine.Object;

    public sealed class ObjectListEmptyStateTests
    {
        private const string NoTypeMessage = "Select a type to list its objects.";

        private static void InvokePrivate(DataVisualizerWindow window, string methodName, params object[] arguments)
        {
            MethodInfo method = typeof(DataVisualizerWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(method != null, $"The {methodName} method must exist.");
            method.Invoke(window, arguments);
        }

        /*
            A fresh window has no selected type, so the Objects pane has no list to describe.
            The old placeholder still formatted the catalog's "No Type" sentinel as if it were a
            real type, which reads as a bug instead of an instruction.
        */
        [Test]
        public void ShouldAskForATypeInsteadOfNamingANonexistentType()
        {
            using TestCleanupScope cleanup = new();
            FieldInfo instance = typeof(DataVisualizerWindow).GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = instance.GetValue(null);
            cleanup.Defer(() => instance.SetValue(null, previous));
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            cleanup.Defer(() => Object.DestroyImmediate(window));
            window.CreateGUI();

            InvokePrivate(window, "RefreshObjectListPresentation");

            VisualElement root = window.rootVisualElement;
            Label empty = root.Q<Label>("empty-object-list-label");
            ListView list = root.Q<ListView>("object-listview");
            Assert.That(empty != null, "The Objects pane needs its empty-state label.");
            Assert.That(list != null, "The Objects pane needs its list.");
            Assert.That(empty.text, Is.EqualTo(NoTypeMessage));
            Assert.That(empty.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(list.style.display.value, Is.EqualTo(DisplayStyle.None));
        }
    }
}
