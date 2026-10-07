using Strategy.Observability.Diagnostics;
using Share.CircuitFramework.Compiler;
using Share.CircuitFramework.Core;
using Share.CircuitFramework.Execution;
using Share.CircuitFramework.Projection;

namespace Strategy.Observability.Ports;

/// <summary>
/// 端口电路执行后端（CF-4，**L4 落点重排后落 L3**）：<see cref="IPortExecutionBackend"/> 的唯一生产实现。
/// <para>
/// 执行面 = 电路引擎拓扑驱动：<see cref="PortSpec.Program"/> 指令序列 → SSA 配方（每指令一节点 + 链式顺序边）
/// → 框架**唯一编译入口** <see cref="CircuitCompiler.TryCompile"/> → <see cref="CircuitEngine"/> 按拓扑序推进；
/// 每节点恰驱动**一条指令**，节点体 = 内核执行单源 <see cref="PortEvalSession"/>
/// （与 <see cref="PortEvaluator"/> 同源 ⇒ 语义不可能分叉）。
/// </para>
/// <para>
/// 驱动分工：求值引擎按**声明序整段**跑；本件把同一序列交给电路引擎按**依赖拓扑**跑（顺序由链式 Wire 固定为声明序）
/// ⇒ 两后端产物应逐项等价（CF-3 对拍面）。
/// </para>
/// <para>空程序（未迁移族 / 不可求值）：无电路可建 ⇒ 回退 <see cref="PortEvaluator"/>（不伪造节点）。</para>
/// </summary>
public sealed class PortCircuitEngine : IPortExecutionBackend
{
    /// <summary>端口指令节点 Kind 前缀（Kind 携带指令序号：<c>port.i{n}</c>，使每节点 Kind 唯一可解析）。</summary>
    private const string NodeKindPrefix = "port.i";

    private readonly PortEvaluator _evaluator;

    /// <summary>按求值引擎构造（缺省 = <see cref="PortEvaluator.Shared"/>）。</summary>
    public PortCircuitEngine(PortEvaluator? evaluator = null) => _evaluator = evaluator ?? PortEvaluator.Shared;

    /// <inheritdoc />
    public async ValueTask<object?> ExecuteAsync(
        PortSpec spec, IPortInput input, PortInjection injection, CancellationToken ct = default)
    {
        PortGuard.Required(spec, nameof(spec));
        PortGuard.Required(input, nameof(input));
        PortGuard.Required(injection, nameof(injection));

        ct.ThrowIfCancellationRequested();

        // 空程序（未迁移族）：无电路可建 ⇒ 回退求值引擎（不伪造电路）。
        if (spec.Program.Count == 0)
            return await _evaluator
                .EvaluateAsync(spec, input, new EvalContext(spec.PortKey), injection, ct)
                .ConfigureAwait(false);

        var projection = Project(spec);
        if (!CircuitCompiler.TryCompile(projection.Recipe, out var compiled, out var diagnostics) || compiled is null)
            throw new PortBindingViolationException(
                spec.PortKey,
                $"端口 '{spec.PortKey}' 的电路编译失败（唯一入口 CircuitCompiler.TryCompile）："
                + string.Join(" | ", diagnostics.Select(d => d.Message)));

        var session = new PortEvalSession(spec, input, new EvalContext(spec.PortKey), injection);

        await CircuitEngine.ExecuteAsync(
                compiled.Circuit,
                compiled.Plan,
                kind => ResolveNode(spec, projection, kind, session),
                entryInputs: null,
                cancellationToken: ct)
            .ConfigureAwait(false);

        return session.Complete();
    }

    /// <summary>Kind → 节点（投影与执行不一致即 fail-fast，不静默）。</summary>
    private static ICircuitNode ResolveNode(
        PortSpec spec, Projection projection, string kind, PortEvalSession session)
        => projection.Nodes.TryGetValue(kind, out var plan)
            ? new InstructionNode(kind, plan.Index, plan.OutPorts, session)
            : throw new PortBindingViolationException(
                spec.PortKey, $"端口 '{spec.PortKey}' 的电路节点 Kind '{kind}' 未解析（投影与执行不一致）。");

    /// <summary>单指令节点计划（指令序号 + 产生产物名）。</summary>
    private sealed record InstructionNodePlan(int Index, IReadOnlyList<string> OutPorts);

    /// <summary>投影产物（配方 + Kind → 节点计划）。</summary>
    private sealed record Projection(
        CircuitRecipeSpec Recipe, IReadOnlyDictionary<string, InstructionNodePlan> Nodes);

    /// <summary>
    /// SSA 投影：指令 i → 节点 <c>ins{i}</c>（Kind <c>port.i{i}</c>）；产生产物 = 显式写槽 / 依赖槽 / 合成槽；
    /// 链式顺序边把「声明序」固化为拓扑序；依赖消费（<c>Call.Dep</c>）经 Wire 接依赖节点。
    /// </summary>
    private static Projection Project(PortSpec spec)
    {
        var steps = new List<CircuitStepSpec>();
        var flows = new List<CircuitFlowSpec>();
        var nodes = new Dictionary<string, InstructionNodePlan>(StringComparer.Ordinal);
        var depNodes = new List<string>();
        var depOrdinal = 0;
        string? prevNode = null;
        string? prevSeq = null;
        string? lastExit = null;

        for (var index = 0; index < spec.Program.Count; index++)
        {
            var instruction = spec.Program[index];
            var nodeId = $"ins{index}";
            var kind = NodeKindPrefix + index;
            var inPorts = new List<string>();
            var outPorts = new List<string>();

            string? depPort = null;
            if (instruction.Op == PortOp.Dep)
            {
                depPort = $"dep{depOrdinal}";
                depNodes.Add(nodeId);
                depOrdinal++;
            }

            var primary = instruction.Out ?? depPort ?? $"out{index}";
            outPorts.Add(primary);

            var seq = $"seq{index}";
            outPorts.Add(seq);
            if (prevSeq is not null) inPorts.Add(prevSeq);

            if (instruction.Op == PortOp.Call
                && instruction.Dep is int depIndex && depIndex >= 0 && depIndex < depNodes.Count)
            {
                inPorts.Add($"dep{depIndex}");
                flows.Add(new CircuitFlowSpec(depNodes[depIndex], $"dep{depIndex}", nodeId, $"dep{depIndex}"));
            }

            steps.Add(new CircuitStepSpec(nodeId, kind, inPorts, outPorts));
            if (prevNode is not null && prevSeq is not null)
                flows.Add(new CircuitFlowSpec(prevNode, prevSeq, nodeId, prevSeq));

            nodes[kind] = new InstructionNodePlan(index, outPorts);
            prevNode = nodeId;
            prevSeq = seq;
            lastExit = $"{nodeId}.{primary}";
        }

        var recipe = new CircuitRecipeSpec(
            spec.PortKey,
            Array.Empty<string>(),
            lastExit is null ? Array.Empty<string>() : new[] { lastExit },
            steps,
            flows);

        return new Projection(recipe, nodes);
    }

    /// <summary>单指令节点：节点体 = 内核执行单源的对应指令（同源 ⇒ 与整段驱动等价）。</summary>
    private sealed class InstructionNode : ICircuitNode
    {
        private readonly int _index;
        private readonly IReadOnlyList<string> _outPorts;
        private readonly PortEvalSession _session;

        public InstructionNode(string kind, int index, IReadOnlyList<string> outPorts, PortEvalSession session)
        {
            NodeType = kind;
            _index = index;
            _outPorts = outPorts;
            _session = session;
        }

        /// <inheritdoc />
        public string NodeType { get; }

        /// <inheritdoc />
        public Task<IReadOnlyDictionary<string, object?>> ExecuteAsync(
            IReadOnlyDictionary<string, object?> inputs, CancellationToken cancellationToken)
        {
            var value = _session.Run(_index);

            var outputs = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var port in _outPorts) outputs[port] = value;

            return Task.FromResult<IReadOnlyDictionary<string, object?>>(outputs);
        }
    }
}
