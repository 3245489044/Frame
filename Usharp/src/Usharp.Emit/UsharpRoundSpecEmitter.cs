using Operation.HierarchyMethod.Nodes.Workflows;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Emit;

/// <summary>
/// <c>[Flow]</c> → <see cref="RoundSpec"/> 发射器：步骤键 → <c>[Node]</c> 可执行体（<c>fn/inports/outports</c>）
/// → <c>RoundStep(FuncName, ParameterKeys, ResultKeys, NodeId=步骤键)</c>，并附链转移表。
/// <para>成功准则经 <c>[Flow]</c> 的 <c>success=</c> 字段声明（缺省 decision），口径与既有骨架加载器一致。</para>
/// <para>原 C1（<c>on_success</c> 之外的分支被静默截断）在语言侧由 US1005/US1006（闸A）编译期拦下；本层只发射链本身。</para>
/// </summary>
public static class UsharpRoundSpecEmitter
{
    /// <summary>发射指定流为骨架契约（未声明 / 步骤缺 <c>[Node]</c> 时产出 error 诊断且不产 Spec）。</summary>
    public static UsharpFlowEmitResult Emit(UsharpCompilation compilation, string flowId)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var diagnostics = new List<UsharpDiagnostic>();
        var flow = compilation.Flows.FirstOrDefault(f => string.Equals(f.FlowId, flowId, StringComparison.Ordinal));
        if (flow is null)
        {
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.FlowNotDeclared,
                $"[Flow] '{flowId}' 未声明（本编译可发射的流：{string.Join(", ", compilation.Flows.Select(f => f.FlowId))}）",
                compilation.Documents.FirstOrDefault()?.FilePath ?? "(request)", 0));
            return new UsharpFlowEmitResult(null, null, diagnostics);
        }

        var nodes = UsharpNodePortReader.Read(compilation.Declarations);
        var steps = BuildSteps(flow, nodes, diagnostics);

        var spec = new RoundSpec(flow.FlowId, steps)
        {
            Source = RoundSpecSource.Declared,
            SuccessCriterion = ParseSuccessCriterion(flow, diagnostics),
        };

        // R3 控制流：装配 [Branch]/[Switch]/[Call] 声明（缺省无声明 = 原样）；复用本处已读的 [Node] 端口表。
        spec = UsharpControlFlowEmitter.Apply(spec, compilation, nodes);

        return new UsharpFlowEmitResult(spec, UsharpTransitionTableBuilder.Build(flow), diagnostics);
    }

    /// <summary>
    /// <c>[Flow]</c> 步骤声明的可执行体装配（**唯一映射点**）：步骤键 → <c>[Node]</c> → <see cref="RoundStep"/>。
    /// 消费方：主流发射（<see cref="Emit"/>）与子流内联（<c>UsharpControlFlowEmitter.BuildChildSpec</c>）——禁各处再拼 <see cref="RoundStep"/>。
    /// </summary>
    /// <param name="flow">流声明（诊断归因来源）。</param>
    /// <param name="nodes">本编译的 <c>[Node]</c> 端口表（<see cref="UsharpNodePortReader.Read"/> 的产物）。</param>
    /// <param name="diagnostics">诊断出口（未映射步骤 ⇒ <c>US5002</c>；null ⇒ 静默跳过，供子流装配不计重复诊断）。</param>
    internal static List<RoundStep> BuildSteps(
        UsharpFlowDecl flow, IReadOnlyDictionary<string, UsharpNodeSpec> nodes, ICollection<UsharpDiagnostic>? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(nodes);

        var steps = new List<RoundStep>(flow.Steps.Count);
        foreach (var step in flow.Steps)
        {
            if (!nodes.TryGetValue(step.Key, out var node))
            {
                diagnostics?.Add(UsharpDiagnostic.Error(UsharpCodes.FlowStepUnmapped,
                    $"[Flow] {flow.FlowId}: 步骤键 '{step.Key}' 无对应 [Node] 声明（无法解析 FuncName，可执行体未登记）",
                    flow.SourceFile, flow.LineNumber));
                continue;
            }
            steps.Add(CreateStep(step, node));
        }
        return steps;
    }

    /// <summary>单步装配（步骤键 = <c>[Node]</c> 名 = <see cref="RoundStep.NodeId"/>；<c>Emits</c>/<c>Kind</c> 随声明透出）。</summary>
    private static RoundStep CreateStep(UsharpFlowStep step, UsharpNodeSpec node)
        => new(node.FuncName, node.InputPorts, node.OutputPorts, NodeId: step.Key)
        {
            Emits = step.Emits,
            Kind = node.Kind,
        };

    /// <summary>解析 <c>success=</c> 字段（<c>run_to_end</c> → RunToEnd；<c>decision</c>/缺省 → DecisionWord；未知值报 US5003 并回退 DecisionWord）。</summary>
    public static RoundSpecSuccessCriterion ParseSuccessCriterion(
        UsharpFlowDecl flow, ICollection<UsharpDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(flow);

        var value = flow.SuccessCriterion?.Trim();
        if (string.IsNullOrEmpty(value)) return RoundSpecSuccessCriterion.DecisionWord;
        if (string.Equals(value, "run_to_end", StringComparison.OrdinalIgnoreCase))
            return RoundSpecSuccessCriterion.RunToEnd;
        if (string.Equals(value, "decision", StringComparison.OrdinalIgnoreCase))
            return RoundSpecSuccessCriterion.DecisionWord;

        diagnostics?.Add(UsharpDiagnostic.Warn(UsharpCodes.SuccessCriterionUnknown,
            $"[Flow] {flow.FlowId}: 未知成功准则 success='{value}'（合法值：decision/run_to_end），按 decision 处理",
            flow.SourceFile, flow.LineNumber));
        return RoundSpecSuccessCriterion.DecisionWord;
    }
}
