namespace Share.ScopeFramework;

/// <summary>
/// 命名/归属派生规则（纯逻辑，下沉 Share 层单一实现，对齐 TH-12 SingleImplementation，不复制）。
/// 权威：任务中心多层作用域封装框架设计 §3.3。
/// </summary>
public interface IScopeNamingRule
{
    /// <summary>派生 workflowId：scope.workflowId ?? taskKey（任务隔离，杜绝共享 agent-round 串任务；可在 scope 显式覆盖）。</summary>
    string ResolveWorkflowId(ScopeContext scope);

    /// <summary>派生 nodeKey：按 nodeType 选择命名空间前缀（nodes/events/keymapping/config）。</summary>
    string ResolveNodeKey(ScopeContext scope, string nodeType, string name);

    /// <summary>派生 ThreadId：自动填 workflowId。</summary>
    string ResolveThreadId(ScopeContext scope);

    /// <summary>nodeKey/related 列表归一化：过滤空白 + Trim + 去重保序（吸收 Agent 的 TrimNodeKeys，单一源）。</summary>
    IReadOnlyList<string> NormalizeNodeKeys(IReadOnlyCollection<string?>? keys);
}
