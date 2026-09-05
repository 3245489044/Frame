#nullable enable

using System.Collections.Generic;
using GraphFoundation.Abstractions;
using Modules.LayoutAnalysis.Models;

namespace Modules.LayoutAnalysis.Models
{
    public class LayoutNodeData
    {
        public NormalizedEntity Entity { get; set; } = new();

        public string Type { get; set; } = "entity";
    }

    public class LayoutEdgeData
    {
        public string Kind { get; set; } = string.Empty;

        public string RelationType { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string Evidence { get; set; } = string.Empty;
    }

    public class LayoutCluster
    {
        public List<string> NodeIds { get; set; } = new();

        public int Count => NodeIds.Count;
    }

    public class LayoutGraphContext
    {
        public IReadOnlyGraph<string, LayoutNodeData, LayoutEdgeData> Graph { get; set; } = default!;

        public IReadOnlyList<LayoutCluster> Clusters { get; set; } = new List<LayoutCluster>();

        public IReadOnlyList<string> EntryNodeIds { get; set; } = new List<string>();

        public IReadOnlyList<string> ExitNodeIds { get; set; } = new List<string>();

        public bool HasCycle { get; set; }
    }
}