namespace Share.CircuitFramework.Projection;

/// <summary>
/// 声明式顺序链配方（无上下文）：步骤表 → 唯一产品链 p1→p2→…（相邻步自动成边）+
/// <see cref="Wire"/> 声明式跨步真依赖 + <see cref="Collect"/> 出口收集；步骤体经 Kind 解析表注入
/// （<see cref="Execution.CircuitNodeResolver"/>），故本类零领域依赖、不持步骤体。
/// <para>适用面：产物图配方（步骤间无相邻顺序语义之外的隐式依赖时，顺序即语义）。</para>
/// </summary>
public sealed class RecipeBuilder
{
    /// <summary>缺省入口端口名（外部绑定产物，如 config）。</summary>
    public const string DefaultEntryPort = RecipeChainCore.DefaultEntryPort;

    private readonly RecipeChainCore _chain;

    /// <param name="circuitId">电路 Id（= 装配 Id）。</param>
    /// <param name="entryPorts">入口端口集（外部绑定产物）；缺省 = <see cref="DefaultEntryPort"/>。
    /// 仅声明 spec.EntryPorts；步骤须显式声明所需入口端口（执行层按步骤 InPorts 同名供给）。</param>
    public RecipeBuilder(string circuitId, IReadOnlyList<string>? entryPorts = null)
        => _chain = new RecipeChainCore(circuitId, entryPorts, entryOnEachStep: false);

    /// <summary>追加一步（顺序即语义）：<paramref name="chainPort"/> 缺省 = <c>p{序号}</c>（自动入 OutPorts 与下游 InPorts）。</summary>
    public RecipeBuilder Step(
        string nodeId,
        string stepKind,
        IReadOnlyList<string>? inPorts = null,
        IReadOnlyList<string>? outPorts = null,
        string? chainPort = null)
    {
        _chain.Add(nodeId, stepKind, inPorts, chainPort, outPorts);
        return this;
    }

    /// <summary>显式附加传递边（跨步真依赖：顺序链只连相邻步）。</summary>
    public RecipeBuilder Wire(string fromNode, string fromPort, string toNode, string toPort)
    {
        _chain.Wire(fromNode, fromPort, toNode, toPort);
        return this;
    }

    /// <summary>常量注入（B3-1）：把 <paramref name="value"/> 直接绑到已声明步骤的入端口（不查上下文）。</summary>
    public RecipeBuilder Literal(string nodeId, string port, object? value)
    {
        _chain.Literal(nodeId, port, value);
        return this;
    }

    /// <summary>区域声明（G0-0）：把子计划（环 / 复用块）挂到已声明步骤内部（父图仍为 DAG）。</summary>
    public RecipeBuilder Region(CircuitRegionSpec region)
    {
        _chain.Region(region);
        return this;
    }

    /// <summary>收集出口产物（未声明则缺省 = 末步链端口）。</summary>
    public RecipeBuilder Collect(string nodeId, string port)
    {
        _chain.Collect(nodeId, port);
        return this;
    }

    /// <summary>声明面：投影输入（同一实例幂等）。</summary>
    public CircuitRecipeSpec Build() => _chain.Build();
}
