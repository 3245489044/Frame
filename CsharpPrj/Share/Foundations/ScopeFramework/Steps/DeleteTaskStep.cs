namespace Share.ScopeFramework;

/// <summary>
/// S6 删任务：主删除（task delete）+ 级联清引用。设计 §4.3。
/// 前置：任务存在（P0 恒真；P1 可结合 query taskStatus 确认）。级联清引用在守卫已装配时追加 removeResource。
/// </summary>
internal sealed class DeleteTaskStep : IStep
{
    public string Id => "S6";

    public bool CanExecute(ScopeContext scope) => true;

    public IReadOnlyList<ScopeAction> ProduceActions(ScopeContext scope, IScopeNamingRule naming)
    {
        var list = new List<ScopeAction>
        {
            new ScopeAction { Action = "task", Op = "delete", TaskKey = scope.TaskKey }
        };
        if (scope.GraphGuardAssembled)
        {
            list.Add(new ScopeAction { Action = "removeResource", ResourceType = "task", Id = scope.TaskKey });
        }
        return list;
    }
}
