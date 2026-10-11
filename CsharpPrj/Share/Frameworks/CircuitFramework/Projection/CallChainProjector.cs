using Share.CircuitFramework.Core;
using Share.CircuitFramework.Symbols;

namespace Share.CircuitFramework.Projection;

/// <summary>可调用的 C# 函数规约：一个函数 = 电路节点，具名入/出端口 = 参数/返回。</summary>
/// <param name="Id">电路内节点 Id（唯一）。</param>
/// <param name="FunctionKind">被表示的函数实现名（如 <c>"context_projection"</c> / <c>"RunWithOutputsAsync"</c>）。</param>
/// <param name="InPorts">入参端口名。</param>
/// <param name="OutPorts">返回/转发端口名。</param>
/// <param name="Code">代码实体引用（G2，可选）：绑定真实类型/方法/签名，使函数流动图与代码对拍。</param>
public sealed record CallableSpec(
    string Id,                          // 电路内节点 Id（唯一）
    string FunctionKind,                // 被表示的函数实现名（如 "context_projection" / "RunWithOutputsAsync"）
    IReadOnlyList<string> InPorts,      // 入参端口名
    IReadOnlyList<string> OutPorts)     // 返回/转发端口名
{
    /// <summary>代码实体引用（G2，可选）：绑定真实类型/方法/签名，使函数流动图与代码对拍。
    /// <para>以 init 属性承载（不增主构造器参数）—— 保持 4 参构造器 IL 兼容。</para></summary>
    public Core.CodeRef? Code { get; init; }
}

/// <summary>参数传递关系：从某个函数的输出端口传到另一函数的输入端口。</summary>
public sealed record ParamFlowSpec(
    string FromNode, string FromPort,
    string ToNode, string ToPort);

/// <summary>函数调用关系：调用者节点 → 被调电路（跨版本唯一，可带 Version）。</summary>
public sealed record CallEdgeSpec(
    string CallerNodeId, string CalleeId, string? Version = null);

/// <summary>一条 C# 函数调用链的声明式规约：函数集合 + 参数传递 + 调用关系。</summary>
public sealed record CallChainSpec(
    string ChainId,
    IReadOnlyList<CallableSpec> Callables,
    IReadOnlyList<ParamFlowSpec> Flows,
    IReadOnlyList<CallEdgeSpec> Calls);

/// <summary>
/// 读侧投影适配层（方向 A）：把 C# 函数调用链的声明式规约 <see cref="CallChainSpec"/>
/// 投影为电路图 <see cref="Circuit"/> —— 节点 = 函数，端口 = 参数/返回，Wire = 参数传递，
/// CallSymbol = 函数调用。框架即调用关系的唯一宿主，本投影器只装配、不回写业务代码。
/// 投影产物交给 CircuitValidator/CircuitCompiler/CircuitCallGraph 做编译校验与影响面分析。
/// </summary>
public static class CallChainProjector
{
    /// <summary>把声明式调用链规约投影为电路。</summary>
    public static Circuit Project(CallChainSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        // G3 统一 IR：调用链的节点/端口/连线走**与配方同一**投影器（CircuitRecipeProjector.Project），
        // 消除「配方世界 vs 调用链世界」两套节点模型（同一份 AppendSteps/AppendFlows 实现）。
        var circuit = CircuitRecipeProjector.Project(ProjectSpec(spec));

        // 调用关系（跨电路引用）→ CallSymbol（CallerNodeId → CalleeRef）；引用轴由 CircuitReferenceGraph 消费。
        foreach (var c in spec.Calls)
            circuit.Append(new CallSymbol
            {
                CallerNodeId = c.CallerNodeId,
                Target = new CalleeRef(c.CalleeId, c.Version),
            });

        return circuit;
    }

    /// <summary>
    /// 调用链规约 → 声明式配方（G3 统一节点模型）：节点 = 函数（可带 <see cref="Core.CodeRef"/>），
    /// 连线 = 参数传递；入口/出口不声明（调用链以 Call 边而非 Entry/Exit 收口）。
    /// </summary>
    public static CircuitRecipeSpec ProjectSpec(CallChainSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var steps = spec.Callables
            .Select(c => new CircuitStepSpec(c.Id, c.FunctionKind, c.InPorts, c.OutPorts) { Code = c.Code })
            .ToList();
        var flows = spec.Flows
            .Select(f => new CircuitFlowSpec(f.FromNode, f.FromPort, f.ToNode, f.ToPort))
            .ToList();

        return new CircuitRecipeSpec(
            spec.ChainId,
            Array.Empty<string>(),
            Array.Empty<string>(),
            steps,
            flows);
    }
}