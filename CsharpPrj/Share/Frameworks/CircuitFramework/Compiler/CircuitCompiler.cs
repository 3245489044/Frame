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

    /// <summary>
    /// 编译期运行时索引（节点 / 入边绑定 / 入口 / 出口）：执行层零重建。
    /// 手工构造的计划（<see cref="ExecutionPlan(string, IReadOnlyList{string})"/>）为 null ⇒ 执行层即时构建等价索引。
    /// </summary>
    public CircuitRuntimeIndex? Index { get; init; }

    public ExecutionPlan() { }

    public ExecutionPlan(string circuitId, IReadOnlyList<string> order)
    {
        CircuitId = circuitId;
        TopologicalOrder = order;
    }

    public ExecutionPlan(string circuitId, IReadOnlyList<string> order, CircuitRuntimeIndex index)
    {
        CircuitId = circuitId;
        TopologicalOrder = order;
        Index = index;
    }
}

/// <summary>
/// 配方编译产物（配方域 → 可执行）：配方域校验 + 投影 + 电路编译的**单点产物**，
/// 供消费方缓存/复用；消费方不得自行重排「校验 → 投影 → 编译」三步（多份并存即漂移面）。
/// </summary>
public sealed record CompiledRecipe(CircuitRecipeSpec Spec, Circuit Circuit, ExecutionPlan Plan);

/// <summary>
/// 编译层：把符号图编译为无递归回边、已拓扑排血的执行 DAG。
/// 先跑图校验（环检测/类型/端口/孤立引用），校验绿才产出执行计划。
/// </summary>
public static class CircuitCompiler
{
    /// <summary>配方域诊断码（产物图违例：产物唯一 / 缺失产物 / 未接线输入）。</summary>
    public const string RecipeDiagnosticCode = "CFG-030";

    /// <summary>不可排序依赖诊断（CFG-002，环检测失配兜底）：两处失败分支共用，防文案双写漂移。</summary>
    private static CircuitDiagnostic UnsortableDependency() =>
        new(DiagnosticSeverity.Error, "CFG-002",
            "存在不可排序的依赖（环已由 CFG-002 环检测拦截，此路仅在环检测失配时可达）。");

    /// <summary>电路图编译（校验绿才产出计划）；非法图直接抛，不进入执行。</summary>
    public static ExecutionPlan Compile(Circuit circuit)
    {
        var diagnostics = CircuitValidator.Validate(circuit);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0)
            throw new CircuitCompilationException(errors);

        var order = TopologicalSort(circuit);
        if (order is null)
            throw new CircuitCompilationException(new[] { UnsortableDependency() });

        return new ExecutionPlan(circuit.Id, order, CircuitRuntimeIndex.Build(circuit));
    }

    /// <summary>
    /// 配方编译（**消费方唯一入口**）：配方域产物图校验 → **区域域校验** → 投影为电路 → 电路编译（环/拓扑）。
    /// 步骤序只此一处（消费方不再各自串联）；失败不抛，由调用方按自身契约决定失败形态（异常类型 / 日志 / 回退）。
    /// </summary>
    /// <param name="spec">声明式装配配方（拓扑单一真源）。</param>
    /// <param name="compiled">成功时的编译产物；失败为 null。</param>
    /// <param name="diagnostics">失败原因（配方域违例为 <see cref="RecipeDiagnosticCode"/>，区域域为 CFG-019~023，电路域为 CFG-0xx）。</param>
    public static bool TryCompile(
        CircuitRecipeSpec spec,
        out CompiledRecipe? compiled,
        out IReadOnlyList<CircuitDiagnostic> diagnostics)
        => TryCompile(spec, out compiled, out diagnostics, depth: 1);

    /// <summary>
    /// 配方编译（内部重载）：<paramref name="depth"/> = 区域嵌套层（1 基），供子计划递归编译限深。
    /// </summary>
    internal static bool TryCompile(
        CircuitRecipeSpec spec,
        out CompiledRecipe? compiled,
        out IReadOnlyList<CircuitDiagnostic> diagnostics,
        int depth)
    {
        ArgumentNullException.ThrowIfNull(spec);
        compiled = null;

        var recipeErrors = RecipeGraphValidator.Validate(spec);
        if (recipeErrors.Count > 0)
        {
            diagnostics = recipeErrors
                .Select(e => new CircuitDiagnostic(DiagnosticSeverity.Error, RecipeDiagnosticCode, e))
                .ToList();
            return false;
        }

        // 区域域判据（CFG-019~023）：映射两端 / 迭代上限 / 子计划可解析一律编译期判红；
        // 子计划经同一 TryCompile（R2），不得裸跑，也不得留到运行期才炸。
        var regionDiagnostics = RegionValidator.Validate(spec, depth);
        if (regionDiagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
        {
            diagnostics = regionDiagnostics;
            return false;
        }

        var circuit = CircuitRecipeProjector.Project(spec);
        var circuitDiagnostics = CircuitValidator.Validate(circuit);
        if (circuitDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            diagnostics = circuitDiagnostics;
            return false;
        }

        var order = TopologicalSort(circuit);
        if (order is null)
        {
            diagnostics = new[] { UnsortableDependency() };
            return false;
        }

        compiled = new CompiledRecipe(spec, circuit, new ExecutionPlan(circuit.Id, order, CircuitRuntimeIndex.Build(circuit)));
        diagnostics = Array.Empty<CircuitDiagnostic>();
        return true;
    }

    /// <summary>配方编译（失败即抛 <see cref="CircuitCompilationException"/>）。</summary>
    public static CompiledRecipe Compile(CircuitRecipeSpec spec)
        => TryCompile(spec, out var compiled, out var diagnostics)
            ? compiled!
            : throw new CircuitCompilationException(diagnostics);

    /// <summary>拓扑排序（Kahn）。依赖边 = Wire 的 From.NodeId → To.NodeId；委托 GraphFoundation 装配只读图后拓扑排序。</summary>
    private static IReadOnlyList<string>? TopologicalSort(Circuit circuit)
    {
        TopologyResult<string>? result = GraphProcessingAdapter.TopologicalSort(circuit);
        if (result is null || result.HasCycle)
            return null;
        return result.SortedNodes.ToList();
    }
}