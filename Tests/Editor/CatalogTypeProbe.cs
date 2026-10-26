namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Reflection;

    // Counts display-attribute probes without changing the delegated type metadata.
    internal sealed class CatalogTypeProbe : TypeDelegator
    {
        public int AttributeChecks { get; private set; }

        public CatalogTypeProbe(Type type)
            : base(type) { }

        public override bool IsDefined(Type attributeType, bool inherit)
        {
            if (attributeType == typeof(CustomDataVisualizationAttribute))
            {
                AttributeChecks++;
            }
            return base.IsDefined(attributeType, inherit);
        }

        public void ResetChecks()
        {
            AttributeChecks = 0;
        }
    }
}
