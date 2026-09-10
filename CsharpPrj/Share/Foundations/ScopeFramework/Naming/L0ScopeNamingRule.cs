namespace Share.ScopeFramework;

/// <summary>
/// L0（runloop/graph/config 无任务作用域）命名规则：workflowId = scope.workflowId ?? "agent-round"（L0 兼容默认）；
/// nodeKey/threadId 派生共享基类，threadId 取 L0 workflowId。
/// 供 Agent 对话/历史等非任务作用域消费方复用单一命名源（消除 Agent 侧散落的 ResolveWorkflowId 双实现）。
/// </summary>
public sealed class L0ScopeNamingRule : ScopeNamingRule
{
    /// <summary>L0 空 workflowId 的默认命名空间归属。</summary>
    private const string DefaultWorkflowId = "agent-round";

    protected override string ResolveWorkflowIdDefault(ScopeContext scope) => ResolveWorkflowId(scope.WorkflowId);

    /// <summary>按裸 workflowId 解析（L0 便捷入口：空/空白 → agent-round，否则 Trim），供无任务作用域的对话/历史消费方。</summary>
    public string ResolveWorkflowId(string? workflowId)
        => string.IsNullOrWhiteSpace(workflowId) ? DefaultWorkflowId : workflowId.Trim();
}
