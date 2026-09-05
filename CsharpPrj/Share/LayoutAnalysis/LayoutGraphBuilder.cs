#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using GraphFoundation.Abstractions;
using GraphFoundation.Core;
using GraphFoundation.Navigation;
using GraphFoundation.Topology;
using Modules.LayoutAnalysis.Models;

namespace Modules.LayoutAnalysis
{
    public class LayoutGraphBuilder
    {
        public const string HierarchyEdgePrefix = "hier";

        public const string SpatialEdgePrefix = "spat";

        public const string SemanticEdgePrefix = "sem";

        public LayoutGraphContext Build(LayoutCollectionResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            var graph = new CoreGraph<string, LayoutNodeData, LayoutEdgeData>();

            foreach (var entity in result.NormalizedEntities)
            {
                if (string.IsNullOrEmpty(entity.Id)) continue;
                if (!graph.ContainsNode(entity.Id))
                {
                    graph.AddNode(entity.Id, new LayoutNodeData { Entity = entity });
                }
            }

            foreach (var rel in result.HierarchyRelations)
            {
                if (string.IsNullOrEmpty(rel.ParentId) || string.IsNullOrEmpty(rel.ChildId)) continue;
                if (!graph.ContainsNode(rel.ParentId) || !graph.ContainsNode(rel.ChildId)) continue;
                AddUniqueEdge(graph, $"{HierarchyEdgePrefix}:{rel.ParentId}->{rel.ChildId}",
                    rel.ParentId, rel.ChildId, new LayoutEdgeData
                    {
                        Kind = LayoutGraphConstants.KindHierarchy,
                        RelationType = LayoutGraphConstants.RelationHierarchy,
                        Description = $"parent container of {rel.ChildId}"
                    });
            }

            foreach (var rel in result.SpatialRelations)
            {
                if (string.IsNullOrEmpty(rel.From) || string.IsNullOrEmpty(rel.To)) continue;
                if (!graph.ContainsNode(rel.From) || !graph.ContainsNode(rel.To)) continue;
                AddUniqueEdge(graph, $"{SpatialEdgePrefix}:{rel.From}->{rel.To}:{rel.Type}",
                    rel.From, rel.To, new LayoutEdgeData
                    {
                        Kind = LayoutGraphConstants.KindSpatial,
                        RelationType = rel.Type,
                        Description = rel.Description
                    });
            }

            foreach (var rel in result.SemanticRelations)
            {
                if (string.IsNullOrEmpty(rel.From) || string.IsNullOrEmpty(rel.To)) continue;
                if (!graph.ContainsNode(rel.From) || !graph.ContainsNode(rel.To)) continue;
                AddUniqueEdge(graph, $"{SemanticEdgePrefix}:{rel.From}->{rel.To}",
                    rel.From, rel.To, new LayoutEdgeData
                    {
                        Kind = LayoutGraphConstants.KindSemantic,
                        RelationType = rel.RelationType,
                        Evidence = rel.Evidence
                    });
            }

            return Analyze(graph);
        }

        private static LayoutGraphContext Analyze(CoreGraph<string, LayoutNodeData, LayoutEdgeData> graph)
        {
            var context = new LayoutGraphContext
            {
                Graph = graph,
                HasCycle = graph.NodeCount() > 0 && GraphTopology.HasCycle(graph)
            };

            var components = GraphTopology.GetConnectedComponents(graph);
            context.Clusters = components
                .Where(c => c.Count > 0)
                .Select(c =>
                {
                    var sorted = SafeTopologicalOrder(graph, c);
                    return new LayoutCluster { NodeIds = sorted };
                })
                .ToList();

            context.EntryNodeIds = graph.GetEntryNodeKeys();
            context.ExitNodeIds = graph.GetExitNodeKeys();

            return context;
        }

        private static List<string> SafeTopologicalOrder(
            IReadOnlyGraph<string, LayoutNodeData, LayoutEdgeData> graph,
            IReadOnlyList<string> componentKeys)
        {
            var keySet = new HashSet<string>(componentKeys);
            try
            {
                var result = GraphTopology.TopologicalSort(graph);
                if (!result.HasCycle)
                {
                    return result.SortedNodes.Where(keySet.Contains).ToList();
                }
            }
            catch
            {
            }
            return componentKeys.ToList();
        }

        private static void AddUniqueEdge(
            CoreGraph<string, LayoutNodeData, LayoutEdgeData> graph,
            string edgeId, string source, string target,
            LayoutEdgeData data)
        {
            if (graph.ContainsEdge(edgeId)) return;
            graph.AddEdge(edgeId, source, target, isDirected: true, data);
        }
    }

    public static class LayoutGraphConstants
    {
        public const string KindHierarchy = "hierarchy";
        public const string KindSpatial = "spatial";
        public const string KindSemantic = "semantic";
        public const string RelationHierarchy = "parent-child";
    }
}