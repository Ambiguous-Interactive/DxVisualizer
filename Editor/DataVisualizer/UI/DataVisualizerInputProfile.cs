namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using UnityEngine;

    /// <summary>An editable input asset shared by windows through their settings asset.</summary>
    [CreateAssetMenu(fileName = "DataVisualizerInputProfile", menuName = "Wallstop Studios/DxVisualizer/Input Profile")]
    public sealed class DataVisualizerInputProfile : ScriptableObject
    {
        public DataVisualizerInputSettings Bindings => bindings ??= new DataVisualizerInputSettings();

        [SerializeField]
        private DataVisualizerInputSettings bindings = new();
    }
}
