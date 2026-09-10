#nullable enable

using System.Collections.Generic;
using System.Linq;
using GraphFoundation.Abstractions;
using GraphFoundation.Core;
using UniversalWorkflowFramework.Core.Models;

namespace UniversalWorkflowFramework.Core.Graph
{
    /// <summary>
    /// UR 模型 → ICoreGraph 适配桥（模型适配路线 A）。节点键 = <see cref="ResourceIdentifier.GetUniqueKey"/>。
    /// 仅做投影/回映射，不对外暴露；供 <see cref="ReferenceGraphEngine"/> 组合 GraphProcessing 使用。
    /// </summary>
    internal sealed class ReferenceGraphAdapter
    {
        public string GetResourceKey(Resource resource) => resource.Identifier.GetUniqueKey();

        public ICoreGraph<string, Resource, object> ToCoreGraph(
            IReadOnlyCollection<Resource> resources,
            IReadOnlyCollection<ReferenceEdge> references)
        {
            var graph = new CoreGraph<string, Resource, object>();

            foreach (var resource in resources)
            {
                var key = GetResourceKey(resource);
                graph.AddNode(key, resource, resource.Symbol);
            }

            var seenEdges = new HashSet<string>();
            foreach (var reference in references)
            {
                if (!graph.ContainsNode(reference.SourceKey) || !graph.ContainsNode(reference.TargetKey))
                    continue;

                var edgeId = BuildEdgeId(reference);
                if (!seenEdges.Add(edgeId))
                    continue;

                graph.AddEdge(edgeId, reference.SourceKey, reference.TargetKey,
                    isDirected: true, data: reference.Data, weight: reference.Strength);
            }

            return graph;
        }

        private static string BuildEdgeId(ReferenceEdge reference)
        {
            var seed = $"{reference.SourceKey}->{reference.TargetKey}[{reference.ReferenceType}]{reference.Description}";
            return seed.GetHashCode().ToString("x");
        }
    }

    /// <summary>适配桥边投影（Source/Target 键 + 边数据）。</summary>
    internal sealed class ReferenceEdge
    {
        public string SourceKey { get; set; } = string.Empty;

        public string TargetKey { get; set; } = string.Empty;

        public string ReferenceType { get; set; } = string.Empty;

        public string? Description { get; set; }

        public double Strength { get; set; } = 1.0;

        public object Data { get; set; } = new();
    }
}
