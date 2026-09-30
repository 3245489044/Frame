using GraphFoundation.Topology;
using Share.CircuitFramework.Core;
using Share.CircuitFramework.Projection;

namespace Share.CircuitFramework.Compiler;

/// <summary>编译产物：已去回边、已拓扑排血的执行 DAG。描述层 → 编译层 的输出。</summary>
public sealed class ExecutionPlan
{
    public string CircuitId { get; init; } = string.Empty;

    /// <summary>按依赖顺序排列的节点执行序列。</summary>
    public IReadOnlyList<string> TopologicalOrder { get; init; } = Array.Empty<string>();

    public ExecutionPlan() { }

    public ExecutionPlan(string circuitId, IReadOnlyList<string> order)
    {
        CircuitId = circuitId;
        TopologicalOrder = order;
    }
}

/// <summary>
/// 编译层：把符号图编译为无递归回边、已拓扑排血的执行 DAG。
/// 先跑图校验（环检测/类型/端口/孤立引用），校验绿才产出执行计划。
/// </summary>
public static class CircuitCompiler
{
    public static ExecutionPlan Compile(Circuit circuit)
    {
        var diagnostics = CircuitValidator.Validate(circuit);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0)
            throw new CircuitCompilationException(errors);

        var order = TopologicalSort(circuit);
        if (order is null)
            throw new CircuitCompilationException(new[]
            {
                new CircuitDiagnostic(DiagnosticSeverity.Error, "CFG-020",
                    "存在不可排序的依赖（应为已由环检测拦截）。"),
            });

        return new ExecutionPlan(circuit.Id, order);
    }

    /// <summary>拓扑排序（Kahn）。依赖边 = Wire 的 From.NodeId → To.NodeId；委托 GraphFoundation 装配只读图后拓扑排序。</summary>
    private static IReadOnlyList<string>? TopologicalSort(Circuit circuit)
    {
        TopologyResult<string>? result = GraphProcessingAdapter.TopologicalSort(circuit);
        if (result is null || result.HasCycle)
            return null;
        return result.SortedNodes.ToList();
    }
}