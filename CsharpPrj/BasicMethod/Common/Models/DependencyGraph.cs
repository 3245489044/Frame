#nullable enable

using System.Collections.Generic;

namespace Common.Models
{
    public class DependencyGraph
    {
        public List<ReferenceRelation> Relations { get; set; } = new();
        public Dictionary<string, List<string>> AdjacencyList { get; set; } = new();

        public void AddNode(string nodeId)
        {
            if (!AdjacencyList.ContainsKey(nodeId))
                AdjacencyList[nodeId] = new List<string>();
        }

        public void AddEdge(string from, string to)
        {
            AddNode(from);
            AdjacencyList[from].Add(to);
        }
    }
}
