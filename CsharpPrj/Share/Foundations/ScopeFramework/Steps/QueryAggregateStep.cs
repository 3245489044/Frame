namespace Share.ScopeFramework;

/// <summary>
/// S4 按任务查节点：query.nodesByTask（结构化载荷）。设计 §4.3 / §5.2。
/// 过滤键 = workflowId（threadId），与 S1 写入侧一致（ResolveThreadId = scope.workflowId ?? taskKey）。
/// P0 仅产出 JSON 描述（后端 nodesByTask 查询在 P1 落地）。
/// </summary>
internal sealed class QueryAggregateStep : IStep
{
    public string Id => "S4";

    public bool CanExecute(ScopeContext scope) => true;

    public IReadOnlyList<ScopeAction> ProduceActions(ScopeContext scope, IScopeNamingRule naming)
        => new[]
        {
            new ScopeAction
            {
                Action = "query",
                Target = "nodesByTask",
                Key = scope.TaskKey,
                ThreadId = naming.ResolveThreadId(scope),
            }
        };
}
