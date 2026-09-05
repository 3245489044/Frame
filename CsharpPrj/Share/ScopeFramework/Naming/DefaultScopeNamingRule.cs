namespace Share.ScopeFramework;

/// <summary>
/// 默认命名规则（任务作用域）：workflowId = scope.workflowId ?? taskKey（任务隔离，杜绝共享 agent-round 串任务；可在 scope 显式覆盖）；
/// nodeKey 按 nodeType 前缀；threadId = workflowId。对齐设计 §3.3 的默认规则表。
/// </summary>
public sealed class DefaultScopeNamingRule : ScopeNamingRule
{
    protected override string ResolveWorkflowIdDefault(ScopeContext scope)
        => string.IsNullOrWhiteSpace(scope.WorkflowId) ? scope.TaskKey ?? string.Empty : scope.WorkflowId;
}
