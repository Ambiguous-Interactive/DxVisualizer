namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using System;

    /// <summary>Stable catalog identity, independent of a realized visual row.</summary>
    public readonly struct CatalogEntry : IEquatable<CatalogEntry>
    {
        public string NamespaceKey { get; }
        public Type Type { get; }
        public bool IsNamespace => Type == null;

        public CatalogEntry(string namespaceKey, Type type)
        {
            NamespaceKey = namespaceKey;
            Type = type;
        }

        public bool Equals(CatalogEntry other) => Type == other.Type && string.Equals(NamespaceKey, other.NamespaceKey, StringComparison.Ordinal);

        public override bool Equals(object other) => other is CatalogEntry entry && Equals(entry);

        public override int GetHashCode() => HashCode.Combine(NamespaceKey, Type);
    }
}
