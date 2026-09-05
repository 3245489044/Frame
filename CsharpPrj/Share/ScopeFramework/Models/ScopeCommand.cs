namespace Share.ScopeFramework;

/// <summary>
/// 一条高层命令（命令自带作用域约束，一次性、命令内生效；无可变跨命令状态，可并发重放）。
/// 语义权威：任务中心多层作用域封装框架设计 §3.2。
/// </summary>
public sealed record ScopeCommand(ScopeContext Scope)
{
    /// <summary>命令类型（createTask/addNode/getNodes/findNode/removeNode/linkNode/deleteTask/select）。</summary>
    public string Cmd => Scope.Cmd;
}

/// <summary>
/// 命令级作用域上下文（输入快照）：命令参数 + 命名派生输入 + 运行期装配状态。
/// 装配状态（GraphGuardAssembled / TaskPoolAssembled）由组装层在构造时注入
/// （Agent 侧读取 AgentUiHost.ReferenceGrounding / TaskPool），使短路裁决（IStep.CanExecute）
/// 保持纯逻辑、可单测。权威：设计 §3.3 与 §4.2。
/// </summary>
public sealed record ScopeContext(
    string Cmd,
    string? TaskKey,
    string? WorkflowId = null,
    string? Name = null,
    string? NodeType = null,
    object? Payload = null,
    IReadOnlyList<string>? RelatedNodes = null,
    bool GraphGuardAssembled = false,
    bool TaskPoolAssembled = false,
    string? RawRequestJson = null,
    // ↓ 执行策略快照（可选，enqueue 透传用；D1 修复：SchedulingMode 空值默认 Deterministic）
    string? Input = null,
    int? Priority = null,
    int? MaxRetries = null,
    string? ExecutorId = null,
    string? SchedulingMode = null,
    bool? Active = null)
{
    /// <summary>派生 workflowId：scope.workflowId ?? taskKey（任务隔离，可显式覆盖）。权威：IScopeNamingRule.ResolveWorkflowId。</summary>
    public string ResolvedWorkflowId()
        => string.IsNullOrWhiteSpace(WorkflowId) ? TaskKey ?? string.Empty : WorkflowId;

    /// <summary>节点类型判别（缺省 node）。</summary>
    public string NodeTypeOrDefault()
        => string.IsNullOrWhiteSpace(NodeType) ? "node" : NodeType!.Trim().ToLowerInvariant();
}

/// <summary>
/// 翻译器执行一次高层命令的聚合结果（对齐 TaskGraphService 的 diagnostics 模式）。
/// </summary>
public sealed record ScopeCommandResult(
    string Cmd,
    IReadOnlyList<string> Results,
    IReadOnlyList<DiagnosticEntry> Diagnostics)
{
    /// <summary>全部展开的既有 action 单 JSON（执行端口收到的逐条请求）。</summary>
    public IReadOnlyList<string> ProducedJson { get; init; } = Array.Empty<string>();
}
