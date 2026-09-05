namespace Share.ScopeFramework;

/// <summary>
/// S5 删节点：主删除（node delete）+ 级联清边（removeResource）。设计 §4.3。
/// scope 内自动派生 nodeKey；级联清边在守卫已装配时追加 removeResource。
/// </summary>
internal sealed class DeleteNodeStep : IStep
{
    public string Id => "S5";

    public bool CanExecute(ScopeContext scope) => true;

    public IReadOnlyList<ScopeAction> ProduceActions(ScopeContext scope, IScopeNamingRule naming)
    {
        var name = scope.Name ?? string.Empty;
        var nodeType = scope.NodeTypeOrDefault();
        var nodeKey = naming.ResolveNodeKey(scope, nodeType, name);
        var list = new List<ScopeAction>
        {
            new ScopeAction { Action = "node", Op = "delete", NodeKey = nodeKey }
        };
        if (scope.GraphGuardAssembled)
        {
            var wf = naming.ResolveWorkflowId(scope);
            var id = $"{wf}.{name}"; // 图端点 id（无前缀）
            list.Add(new ScopeAction { Action = "removeResource", ResourceType = "node", Id = id });
        }
        return list;
    }
}
