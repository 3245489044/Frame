#nullable enable

using System.Collections.Generic;

namespace UniversalWorkflowFramework.Reference.Models
{
    public class DependencyGraph
    {
        public string RootResourceId { get; set; } = string.Empty;
        public Dictionary<string, DependencyNode> Nodes { get; set; } = new();
        public List<DependencyEdge> Edges { get; set; } = new();
    }

    public class DependencyNode
    {
        public string ResourceId { get; set; } = string.Empty;
        public string ResourceType { get; set; } = string.Empty;
        public int Depth { get; set; }
        public bool IsCircular { get; set; }
    }

    public class DependencyEdge
    {
        public string SourceId { get; set; } = string.Empty;
        public string TargetId { get; set; } = string.Empty;
        public ReferenceType ReferenceType { get; set; }
        public double Strength { get; set; }
    }
}