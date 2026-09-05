#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GraphFoundation.Abstractions;
using Modules.LayoutAnalysis.Models;

namespace Modules.LayoutAnalysis.Formatting
{
    public static class LayoutGraphFormatter
    {
        public static string GenerateSummary(LayoutGraphContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            var sb = new StringBuilder();

            var nodeCount = context.Graph.GetAllNodeKeys().Count;
            var edgeCount = context.Graph.GetAllEdgeIds().Count;

            sb.AppendLine($"布局图摘要：{nodeCount} 个节点，{edgeCount} 条边");

            if (context.HasCycle)
            {
                sb.AppendLine("⚠ 检测到环（父子/空间关系存在循环引用）");
            }

            if (context.EntryNodeIds.Any())
            {
                sb.AppendLine($"入口节点（根容器）：{string.Join("、", context.EntryNodeIds)}");
            }

            if (context.ExitNodeIds.Any())
            {
                sb.AppendLine($"出口节点（叶子控件）：{string.Join("、", context.ExitNodeIds)}");
            }

            if (context.Clusters.Count > 0)
            {
                sb.AppendLine($"视觉簇（连通分量）：{context.Clusters.Count} 个");
                for (int i = 0; i < context.Clusters.Count; i++)
                {
                    var cluster = context.Clusters[i];
                    sb.AppendLine($"  簇 {i + 1}（{cluster.Count} 个节点）：{string.Join(" → ", cluster.NodeIds)}");
                }
            }

            return sb.ToString();
        }

        public static string GenerateJson(LayoutGraphContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            var edges = context.Graph.GetAllEdgeIds().Select(edgeId =>
            {
                var data = context.Graph.GetEdgeData(edgeId);
                return new
                {
                    id = edgeId,
                    source = context.Graph.GetEdgeSource(edgeId),
                    target = context.Graph.GetEdgeTarget(edgeId),
                    kind = data?.Kind,
                    relationType = data?.RelationType,
                    description = data?.Description,
                    evidence = data?.Evidence
                };
            }).ToList();

            var clusters = context.Clusters.Select(c => new
            {
                nodeIds = c.NodeIds
            }).ToList();

            return System.Text.Json.JsonSerializer.Serialize(new
            {
                nodeCount = context.Graph.GetAllNodeKeys().Count,
                edgeCount = edges.Count,
                hasCycle = context.HasCycle,
                entryNodeIds = context.EntryNodeIds,
                exitNodeIds = context.ExitNodeIds,
                clusters,
                edges
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
    }
}