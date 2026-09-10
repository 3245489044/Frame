namespace Share.ScopeFramework;

/// <summary>
/// 命名规则抽象基类：统一实现 nodeKey 派生、threadId 派生与 nodeKey 列表归一化（单一源，避免各命名策略重复），
/// 仅"空 workflowId 的默认派生"由子类策略决定（任务隔离 taskKey / L0 agent-round）。
/// 权威：任务中心多层作用域封装框架设计 §3.3。
/// </summary>
public abstract class ScopeNamingRule : IScopeNamingRule
{
    /// <summary>空 workflowId 时派生的完整 workflowId（含默认值与本策略的 Trim 行为，由子类实现）。</summary>
    protected abstract string ResolveWorkflowIdDefault(ScopeContext scope);

    public string ResolveWorkflowId(ScopeContext scope) => ResolveWorkflowIdDefault(scope);

    public string ResolveNodeKey(ScopeContext scope, string nodeType, string name)
    {
        var wf = ResolveWorkflowId(scope);
        var nt = string.IsNullOrWhiteSpace(nodeType) ? "node" : nodeType.Trim().ToLowerInvariant();
        return nt switch
        {
            "event" => $"events.{wf}.{name}",
            "keymapping" => $"keymapping.{wf}.{name}",
            "config" => $"config.{name}", // 全局键值，不按任务命名空间
            _ => $"nodes.{wf}.{name}",    // output/resource/node/input/checkpoint 默认 nodes 前缀
        };
    }

    public string ResolveThreadId(ScopeContext scope) => ResolveWorkflowId(scope);

    /// <summary>归一化 nodeKey/related 列表：过滤空白 + Trim + 去重保序。对齐 Agent TrimNodeKeys（单一源，消除双实现）。</summary>
    public IReadOnlyList<string> NormalizeNodeKeys(IReadOnlyCollection<string?>? keys)
        => keys?.Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray()
            ?? Array.Empty<string>();
}
