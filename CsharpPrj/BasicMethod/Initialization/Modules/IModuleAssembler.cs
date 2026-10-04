namespace BasicMethod.Initialization.Modules;

/// <summary>模块初始化编排器：依赖拓扑序 + 阶段化 + 失败分类。</summary>
public interface IModuleAssembler
{
    /// <summary>登记模块节点。</summary>
    void Add(IModuleNode node);

    /// <summary>按依赖拓扑序装配全部模块，返回聚合结果（不就地抛：装配缺陷与运行失败分类可见）。</summary>
    ValueTask<AssemblyResult> AssembleAsync(CancellationToken ct);
}
