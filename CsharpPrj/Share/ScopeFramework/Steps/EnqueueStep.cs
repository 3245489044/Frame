namespace Share.ScopeFramework;

/// <summary>
/// S7 入队激活：task op=enqueue（写 ur_tasks 即激活消费循环）。设计 §4.3 / §4.5。
/// 前置：任务池已装配（TaskPoolAssembled）。产物对齐 TaskPoolEnqueueAsync 的关键参数。
/// createTask 路径：taskKey 由命令自带并作为归属命名空间；relatedNodes 随入队传给写透守卫建边。
/// </summary>
internal sealed class EnqueueStep : IStep
{
    public string Id => "S7";

    public bool CanExecute(ScopeContext scope) => scope.TaskPoolAssembled;

    public IReadOnlyList<ScopeAction> ProduceActions(ScopeContext scope, IScopeNamingRule naming)
    {
        // D1 修复：调度模式空值默认 Deterministic（零 LLM 纯簿记），不再硬编码 Hybrid（对齐 TaskPoolEnqueueAsync 权威默认，单一源 ScopeEnqueueDefaults）。
        var mode = string.IsNullOrWhiteSpace(scope.SchedulingMode) ? ScopeEnqueueDefaults.DefaultSchedulingMode : scope.SchedulingMode.Trim();
        // P2：relatedNodes 经框架命名规则归一化（过滤空白 + Trim + 去重保序），与 addNode/attach 入队语义一致（单一源 NormalizeNodeKeys）。
        var related = naming.NormalizeNodeKeys(scope.RelatedNodes);
        return new[]
        {
            new ScopeAction
            {
                Action = "task",
                Op = "enqueue",
                TaskKey = scope.TaskKey,
                Input = scope.Input ?? string.Empty,
                Mode = mode,
                ThreadId = naming.ResolveThreadId(scope),
                RelatedNodes = related.Count > 0 ? related : null,
                Priority = scope.Priority,
                MaxRetries = scope.MaxRetries,
                ExecutorId = scope.ExecutorId,
                Active = scope.Active,
            }
        };
    }
}
