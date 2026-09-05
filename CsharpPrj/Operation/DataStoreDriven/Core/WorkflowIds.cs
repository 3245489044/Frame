using Operation.DataStoreDriven.Abstractions;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 工作流 Id 解析单一权威：收敛 <see cref="NodeGraphReconciler"/> / <see cref="ReferenceGrounding"/> / <see cref="WriteGuardPort"/>
/// 重复的 <c>DefaultWorkflowId</c> 常量与「空 ThreadId → 默认工作流 Id」回退逻辑（RedundancyElimination / SingleImplementation / TH-12）。
/// 镜像 <c>NodeKeys.DefaultWorkflowId="agent-round"</c>（Agent 层）；DataStoreDriven 保持 Agent-无关仅镜像值。
/// 单一真源消除「写透注入 workflowId」与「对账按 node.ThreadId」两套解析导致的图镜像 id 漂移（E-类风险）。
/// </summary>
internal static class WorkflowIds
{
    /// <summary>空 ThreadId 回退的默认工作流 Id。</summary>
    public const string Default = "agent-round";

    /// <summary>空 ThreadId 回退默认工作流 Id。</summary>
    public static string Resolve(string? threadId)
        => string.IsNullOrWhiteSpace(threadId) ? Default : threadId;

    /// <summary>按节点记录回退默认工作流 Id（业务节点以自身 ThreadId 为准，缺失回退默认）。</summary>
    public static string Resolve(NodeRecord node)
        => Resolve(node.ThreadId);
}
