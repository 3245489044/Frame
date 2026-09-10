#nullable enable
using System;

namespace FrameworkAbstractions.ComponentRegistry
{
    public class ComponentRegistration
    {
        public Type ServiceType { get; set; } = null!;
        public string ServiceName { get; set; } = string.Empty;
        public object? Instance { get; set; }
        public CriticalityLevel Criticality { get; set; } = CriticalityLevel.Medium;
        public string? Description { get; set; }
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
        public bool IsRegistered => Instance != null;
    }
}
