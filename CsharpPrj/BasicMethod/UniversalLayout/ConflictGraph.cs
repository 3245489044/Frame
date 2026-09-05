#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UniversalLayoutMath
{

    public class ConflictNode
    {
        public string Name { get; set; } = string.Empty;
        public Rect Rect { get; set; }
        public int ZOrder { get; set; }
        public int ConflictDegree { get; set; }
        public int TotalOverlapArea { get; set; }
        public double MaxOverlapRatio { get; set; }
        public int OriginalWidth { get; set; }
        public int OriginalHeight { get; set; }
    }

    public class ConflictEdge
    {
        public string ElementA { get; set; } = string.Empty;
        public string ElementB { get; set; } = string.Empty;
        public int OverlapArea { get; set; }
        public double OverlapRatio { get; set; }
        public OverlapLevel Level { get; set; }
        public Rect OverlapRect { get; set; }
    }


    public class ConflictGraph
    {
        public Dictionary<string, ConflictNode> Nodes { get; } = new Dictionary<string, ConflictNode>();
        public List<ConflictEdge> Edges { get; } = new List<ConflictEdge>();

        public static ConflictGraph BuildFromReport(OverlapReport report, List<OverlapMath.ElementDesc> elements,
            Dictionary<string, int>? zOrderMap = null)
        {
            var graph = new ConflictGraph();

            var elementMap = new Dictionary<string, OverlapMath.ElementDesc>();
            foreach (var element in elements)
            {
                elementMap[element.Name] = element;
                int zOrder = 0;
                if (zOrderMap != null && zOrderMap.TryGetValue(element.Name, out int zo))
                    zOrder = zo;
                graph.Nodes[element.Name] = new ConflictNode
                {
                    Name = element.Name,
                    Rect = element.Rect,
                    ZOrder = zOrder,
                    OriginalWidth = element.Rect.Width,
                    OriginalHeight = element.Rect.Height
                };
            }

            foreach (var overlap in report.Overlaps)
            {
                graph.Edges.Add(new ConflictEdge
                {
                    ElementA = overlap.ElementA,
                    ElementB = overlap.ElementB,
                    OverlapArea = overlap.OverlapArea,
                    OverlapRatio = overlap.OverlapRatio,
                    Level = overlap.Level,
                    OverlapRect = overlap.OverlapRect
                });

                if (graph.Nodes.TryGetValue(overlap.ElementA, out var nodeA))
                {
                    nodeA.ConflictDegree++;
                    nodeA.TotalOverlapArea += overlap.OverlapArea;
                    nodeA.MaxOverlapRatio = Math.Max(nodeA.MaxOverlapRatio, overlap.OverlapRatio);
                }
                if (graph.Nodes.TryGetValue(overlap.ElementB, out var nodeB))
                {
                    nodeB.ConflictDegree++;
                    nodeB.TotalOverlapArea += overlap.OverlapArea;
                    nodeB.MaxOverlapRatio = Math.Max(nodeB.MaxOverlapRatio, overlap.OverlapRatio);
                }
            }

            return graph;
        }

        public List<string> GetPriorityOrder()
        {
            return Nodes.Values
                .OrderByDescending(n => n.ConflictDegree)
                .ThenByDescending(n => n.TotalOverlapArea)
                .Select(n => n.Name)
                .ToList();
        }

        public List<ConflictEdge> GetEdgesForElement(string elementName)
        {
            return Edges
                .Where(e => e.ElementA == elementName || e.ElementB == elementName)
                .ToList();
        }

        public string GetSummary()
        {
            if (Nodes.Count == 0) return "无冲突";
            var maxConflict = Nodes.Values.Max(n => n.ConflictDegree);
            var maxConflictNode = Nodes.Values.First(n => n.ConflictDegree == maxConflict);
            return $"冲突图: {Nodes.Count} 节点, {Edges.Count} 边, " +
                   $"最高冲突度={maxConflict} (元素 '{maxConflictNode.Name}')";
        }

        public List<List<string>> GetConnectedComponents()
        {
            var visited = new HashSet<string>();
            var components = new List<List<string>>();

            foreach (var nodeName in Nodes.Keys)
            {
                if (visited.Contains(nodeName)) continue;

                var component = new List<string>();
                var queue = new Queue<string>();
                queue.Enqueue(nodeName);
                visited.Add(nodeName);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    component.Add(current);

                    var edges = GetEdgesForElement(current);
                    foreach (var edge in edges)
                    {
                        var neighbor = edge.ElementA == current ? edge.ElementB : edge.ElementA;
                        if (!visited.Contains(neighbor))
                        {
                            visited.Add(neighbor);
                            queue.Enqueue(neighbor);
                        }
                    }
                }

                components.Add(component);
            }

            return components;
        }

        public ConflictNode? GetMostConflictedNode()
        {
            return Nodes.Values
                .OrderByDescending(n => n.ConflictDegree)
                .ThenByDescending(n => n.TotalOverlapArea)
                .FirstOrDefault();
        }

        public double GetDensity()
        {
            int n = Nodes.Count;
            if (n <= 1) return 0;
            int maxEdges = n * (n - 1) / 2;
            return (double)Edges.Count / maxEdges;
        }
    }
}
