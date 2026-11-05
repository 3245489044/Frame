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

        /// <summary>诊断袋（唯一收集实现；出口 = <see cref="Diagnostics"/> 字符串投影）。</summary>
        public ContractFramework.Diagnostics.DiagnosticBag DiagnosticBag { get; } =
            new(LayoutGraphConstants.Codes);

        /// <summary>分析期诊断（拓扑排序失败/成环回退等；空 ⇒ 无异常，禁静默吞）。</summary>
        public IReadOnlyList<string> Diagnostics => DiagnosticBag.ToLines();
    }
}