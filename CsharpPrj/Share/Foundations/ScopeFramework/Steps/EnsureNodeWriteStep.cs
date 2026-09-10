namespace Share.ScopeFramework;

/// <summary>
/// S1 写节点：确保 ur_nodes 存在（幂等 upsert）。设计 §4.3。
/// 前置：payload 非空（无载荷则无内容可写，短路跳过）。
/// </summary>
internal sealed class EnsureNodeWriteStep : IStep
{
    public string Id => "S1";

    public bool CanExecute(ScopeContext scope) => scope.Payload is not null;

    public IReadOnlyList<ScopeAction> ProduceActions(ScopeContext scope, IScopeNamingRule naming)
    {
        var name = scope.Name ?? string.Empty;
        var nodeType = scope.NodeTypeOrDefault();
        var nodeKey = naming.ResolveNodeKey(scope, nodeType, name);
        return new[]
        {
            new ScopeAction
            {
                Action = "node",
                Op = "write",
                NodeKey = nodeKey,
                NodeType = nodeType,
                ThreadId = naming.ResolveThreadId(scope),
                Payload = scope.Payload,
            }
        };
    }
}
