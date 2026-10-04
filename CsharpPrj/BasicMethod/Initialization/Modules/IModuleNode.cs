namespace BasicMethod.Initialization.Modules;

/// <summary>
/// 可装配模块节点：自持依赖拓扑（<see cref="DependsOn"/>）与同层次序（<see cref="Order"/>），
/// 由 <c>ModuleAssembler</c> 依据依赖拓扑序编排初始化（替代「注册顺序即初始化顺序」）。
/// </summary>
public interface IModuleNode : IAsyncInitializable
{
    /// <summary>模块唯一名（装配器内唯一；依赖引用以名为准）。</summary>
    string Name { get; }

    /// <summary>本模块依赖的其它模块名（用于拓扑排序）。</summary>
    IReadOnlyList<string> DependsOn { get; }

    /// <summary>同层内次序（拓扑并行层内稳定排序用）。</summary>
    int Order { get; }

    /// <summary>关停（逆序调用）。</summary>
    ValueTask ShutdownAsync(CancellationToken ct);
}
