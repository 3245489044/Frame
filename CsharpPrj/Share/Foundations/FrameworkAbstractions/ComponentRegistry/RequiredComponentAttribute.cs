#nullable enable
using System;

namespace FrameworkAbstractions.ComponentRegistry
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class RequiredComponentAttribute : Attribute
    {
        public string ComponentName { get; }

        public CriticalityLevel Criticality { get; set; } = CriticalityLevel.Medium;

        public string? Description { get; set; }

        public RequiredComponentAttribute(string componentName)
        {
            if (string.IsNullOrWhiteSpace(componentName))
                throw new ArgumentException("组件名称不能为空", nameof(componentName));
            ComponentName = componentName;
        }
    }
}
