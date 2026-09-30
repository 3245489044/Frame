using Share.CircuitFramework.Core;
using Share.CircuitFramework.Symbols;

namespace Share.CircuitFramework.Projection;

/// <summary>可调用的 C# 函数规约：一个函数 = 电路节点，具名入/出端口 = 参数/返回。</summary>
public sealed record CallableSpec(
    string Id,                          // 电路内节点 Id（唯一）
    string FunctionKind,                // 被表示的函数实现名（如 "context_projection" / "RunWithOutputsAsync"）
    IReadOnlyList<string> InPorts,      // 入参端口名
    IReadOnlyList<string> OutPorts);    // 返回/转发端口名

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

        var circuit = new Circuit(spec.ChainId);

        // 函数 → 电路节点（具名端口）+ 参数传递关系 → Wire：
        // 复用配方投影器的节点/连线装配（装配只有一份实现，调用链投影不另起一套）。
        CircuitRecipeProjector.AppendSteps(circuit, spec.Callables
            .Select(c => new CircuitStepSpec(c.Id, c.FunctionKind, c.InPorts, c.OutPorts))
            .ToList());
        CircuitRecipeProjector.AppendFlows(circuit, spec.Flows
            .Select(f => new CircuitFlowSpec(f.FromNode, f.FromPort, f.ToNode, f.ToPort))
            .ToList());

        // 函数调用关系 → CallSymbol（CallerNodeId → CalleeRef）
        foreach (var c in spec.Calls)
            circuit.Append(new CallSymbol
            {
                CallerNodeId = c.CallerNodeId,
                Target = new CalleeRef(c.CalleeId, c.Version),
            });

        return circuit;
    }
}