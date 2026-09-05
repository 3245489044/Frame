namespace Share.ScopeFramework;

/// <summary>
/// S2 建图端点：确保 node 图资源端点存在（addResource）。设计 §4.3。
/// 前置短路：nodeType=node 且守卫已装配时（WriteGuardPort.Put 自动调 EnsureNodeMirrorAsync 建端点）→ S2 跳过；
/// 否则（nodeType≠node ∨ 守卫未装配）→ 需手动 addResource 补建。
/// 图资源镜像端点 id 采用 {wf}.{name}（无前缀），与 nodeKey nodes.{wf}.{name} 以同一 (wf,name) 归属对关联。
/// </summary>
internal sealed class EnsureGraphEndpointStep : IStep
{
    public string Id => "S2";

    public bool CanExecute(ScopeContext scope)
        => scope.NodeTypeOrDefault() != "node" || !scope.GraphGuardAssembled;

    public IReadOnlyList<ScopeAction> ProduceActions(ScopeContext scope, IScopeNamingRule naming)
    {
        var wf = naming.ResolveWorkflowId(scope);
        var name = scope.Name ?? string.Empty;
        var id = $"{wf}.{name}";
        return new[]
        {
            new ScopeAction { Action = "addResource", ResourceType = "node", Id = id, Name = name }
        };
    }
}
