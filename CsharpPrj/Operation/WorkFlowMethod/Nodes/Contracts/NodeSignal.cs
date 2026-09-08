namespace Operation.WorkFlowMethod.Nodes.Contracts;

/// <summary>
/// 节点信号 NodeSignal ——「节点 + 信号驱动」的最近/价值/区域评判载体（解决「最近」的多维价值评判）。
/// 节点落库时（NodeWritePipeline 写 ur_nodes + cross_db_link 边）同步产出，携带摘要与排序依据，
/// 使不相关区域无需读正文即可判断「是否有价值」；只有激活区域才读正文（read_nodes）。
/// 排序依据 rank = sticky_first → (valueScore desc) → (freshness desc)（spec §五）。
/// </summary>
/// <param name="NodeKey">节点定位（nodeKey）。</param>
/// <param name="WorkflowId">节点命名空间（工作流）。</param>
/// <param name="Module">功能模块（execution/task/plan/decide/...）。</param>
/// <param name="Region">区域（由 module × taskKey 派生，如 task.proj-x）。</param>
/// <param name="NodeType">节点类别（execution_node/task_node/...）。</param>
/// <param name="Freshness">新鲜度（落库时间戳，Unix 秒）。</param>
/// <param name="ValueScore">价值评分（0..1；可空）。</param>
/// <param name="OutputDigest">输出摘要（正文截断摘要，免读正文）。</param>
/// <param name="Sticky">显式焦点标记（恒最前）。</param>
/// <param name="Priority">显式优先级（越大越前）。</param>
public sealed record NodeSignal(
    string NodeKey,
    string WorkflowId,
    string? Module = null,
    string? Region = null,
    string? NodeType = null,
    long Freshness = 0,
    double? ValueScore = null,
    string? OutputDigest = null,
    bool Sticky = false,
    int Priority = 0)
{
    /// <summary>空节点信号（未装配/未命中时的兜底，不抛、不阻断）。</summary>
    public static NodeSignal Empty(string nodeKey, string workflowId = "agent-round")
        => new(nodeKey, workflowId);
}
