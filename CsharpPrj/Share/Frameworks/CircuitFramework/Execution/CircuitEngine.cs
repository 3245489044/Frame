using Modules.Pipeline;
using Share.CircuitFramework.Compiler;
using Share.CircuitFramework.Core;
using Share.CircuitFramework.Symbols;

namespace Share.CircuitFramework.Execution;

/// <summary>
/// 执行层：按拓扑推进节点。复用 Modules.Pipeline：
///   PipelineContext 传数据（节点输出写入 SharedData，键 "$NodeId.$Port"），
///   StagePipeline 做顺序骨架（每节点注册为一个 IPipelineStage<PipelineContext>）。
/// 不重新发明运行编排——pipeline 是执行后端，电路框架是它的上层描述/编译层。
/// </summary>
public static class CircuitEngine
{
    /// <summary>
    /// 执行已编译电路。外部输入按 Entry 端口名提供，返回按 Exit 端口名收集的结果。
    /// </summary>
    /// <param name="resolveNode">按节点 Kind 解析黑盒实现。</param>
    public static async Task<IReadOnlyDictionary<string, object?>> ExecuteAsync(
        Circuit circuit,
        ExecutionPlan plan,
        Func<string, ICircuitNode> resolveNode,
        IReadOnlyDictionary<string, object?>? entryInputs = null,
        CancellationToken cancellationToken = default,
        StagePipeline<PipelineContext>? skeleton = null)
    {
        var ctx = new PipelineContext(cancellationToken);
        var stagePipeline = skeleton ?? new StagePipeline<PipelineContext>(circuit.Id);

        if (entryInputs is not null)
        {
            // 入口投影：Entry 端口名 → 供给对应节点的同名输入端口
            foreach (var entry in circuit.Symbols.OfType<EntrySymbol>())
            {
                if (entryInputs.TryGetValue(entry.Port, out var value))
                {
                    ctx.SharedData[$"$entry.{entry.Port}"] = value;
                }
            }
        }

        foreach (var nodeId in plan.TopologicalOrder)
        {
            var node = circuit.Nodes.Single(n => n.Id == nodeId);
            var impl = resolveNode(node.Kind);
            stagePipeline.Register(new CircuitNodeStage(node, impl, circuit, entryInputs));
        }

        // 节点输出写入上下文 SharedData 键 "$NodeId.$Port"
        await stagePipeline.ExecuteAsync(ctx, cancellationToken);

        // 出口投影：Exit 端口名 "NodeId.Port" → 对应节点输出
        var result = new Dictionary<string, object?>();
        foreach (var exit in circuit.Symbols.OfType<ExitSymbol>())
        {
            string[] parts = exit.Port.Split('.', 2);
            var nodeId = parts[0].Trim();
            var portName = parts.Length == 2 ? parts[1].Trim() : string.Empty;

            // #5 Fix：Exit.Port 必须形如 "NodeId.Port" 且节点/端口真实存在，否则显式诊断，杜绝静默无结果。
            var node = circuit.Nodes.FirstOrDefault(n => n.Id == nodeId);
            if (node is null)
                throw new InvalidOperationException(
                    $"Exit 端口 '{exit.Port}' 引用不存在的节点 '{nodeId}'（Exit 须用 \"NodeId.Port\" 格式）。");
            if (node.OutPorts.All(p => p.Name != portName))
                throw new InvalidOperationException(
                    $"Exit 端口 '{exit.Port}' 引用不存在于节点 '{nodeId}' 的输出端口 '{portName}'。");

            if (ctx.SharedData.TryGetValue($"${exit.Port}", out var value))
                result[exit.Port] = value;
        }
        return result;
    }

    private sealed class CircuitNodeStage : IPipelineStage<PipelineContext>
    {
        private readonly CircuitNode _node;
        private readonly ICircuitNode _impl;
        private readonly Circuit _circuit;
        private readonly IReadOnlyDictionary<string, object?>? _entryInputs;

        public string Name => _node.Id;
        public string Description => $"circuit node {_node.Id} ({_node.Kind})";
        public bool CanExecute(PipelineContext context) => !context.IsCancelled;

        public CircuitNodeStage(CircuitNode node, ICircuitNode impl, Circuit circuit, IReadOnlyDictionary<string, object?>? entryInputs)
        {
            _node = node;
            _impl = impl;
            _circuit = circuit;
            _entryInputs = entryInputs;
        }

        public async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken)
        {
            var inputs = new Dictionary<string, object?>();
            foreach (var wire in _circuit.Wires)
            {
                if (wire.To.NodeId != _node.Id) continue;
                var srcKey = $"${wire.From.NodeId}.{wire.From.PortName}";
                if (context.SharedData.TryGetValue(srcKey, out var v))
                    inputs[wire.To.PortName] = v;
            }
            // 入口绑定
            foreach (var entry in _circuit.Symbols.OfType<EntrySymbol>())
            {
                if (_node.InPorts.Any(p => p.Name == entry.Port)
                    && _entryInputs is not null
                    && _entryInputs.TryGetValue(entry.Port, out var v))
                    inputs[entry.Port] = v;
            }

            var outputs = await _impl.ExecuteAsync(inputs, cancellationToken);
            foreach (var kv in outputs)
                context.SharedData[$"${_node.Id}.{kv.Key}"] = kv.Value;
            context.SetStageResult(_node.Id, true);
        }
    }
}