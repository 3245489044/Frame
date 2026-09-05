namespace Share.ScopeFramework;

/// <summary>
/// 默认命令转移表：高层命令 → 固定步骤路径。未命中或无作用域返回默认 S0 直通；
/// 可经 <see cref="TryRegister"/> 扩展/覆盖自定义命令路径。
/// 语义权威：任务中心多层作用域封装框架设计 §4.4。
/// </summary>
public sealed class ScopeCommandPathRegistry : IScopeCommandPathRegistry
{
    private readonly Dictionary<string, Func<ScopeCommand, IReadOnlyList<IStep>>> _paths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>TryRegister* 语义：注册特定命令路径；命名冲突返回 false 不抛、原路径不变。</summary>
    public bool TryRegister(string cmd, Func<ScopeCommand, IReadOnlyList<IStep>> path)
        => _paths.TryAdd(cmd.Trim().ToLowerInvariant(), path);

    /// <summary>解析一条命令的步骤路径。命令无作用域 → 默认 S0 直通；未注册 → 默认 S0 直通。</summary>
    public IReadOnlyList<IStep> Resolve(ScopeCommand command)
    {
        var scope = command.Scope;
        if (string.IsNullOrWhiteSpace(scope.TaskKey))
            return new[] { new PassThroughStep() }; // L0 无作用域：原样直通

        var cmd = scope.Cmd.Trim().ToLowerInvariant();
        return _paths.TryGetValue(cmd, out var path)
            ? path(command)
            : new[] { new PassThroughStep() };
    }

    /// <summary>内置默认转移表：高层命令 → 固定步骤路径（设计 §4.4）。步骤无状态，可静态单例复用。</summary>
    public static ScopeCommandPathRegistry CreateDefault()
    {
        var reg = new ScopeCommandPathRegistry();
        reg.TryRegister("createtask",
            c => c.Scope.RelatedNodes is { Count: > 0 }
                ? new IStep[] { new EnsureTaskNodeEdgeStep(), new EnqueueStep() }
                : new IStep[] { new EnqueueStep() });
        reg.TryRegister("addnode",
            _ => new IStep[] { new EnsureNodeWriteStep(), new EnsureGraphEndpointStep(), new EnsureTaskNodeEdgeStep() });
        reg.TryRegister("getnodes", _ => new IStep[] { new QueryAggregateStep() });
        reg.TryRegister("findnode", _ => new IStep[] { new QueryAggregateStep() });
        reg.TryRegister("removenode", _ => new IStep[] { new DeleteNodeStep() });
        reg.TryRegister("linknode", _ => new IStep[] { new EnsureGraphEndpointStep(), new EnsureTaskNodeEdgeStep() });
        reg.TryRegister("deletetask", _ => new IStep[] { new DeleteTaskStep() });
        reg.TryRegister("select", _ => Array.Empty<IStep>()); // select 仅校验 + 回显 Preview（dry-run）
        return reg;
    }
}
