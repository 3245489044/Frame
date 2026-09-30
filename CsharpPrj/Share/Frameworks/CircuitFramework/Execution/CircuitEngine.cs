using Modules.Pipeline;
using Share.CircuitFramework.Compiler;
using Share.CircuitFramework.Core;

namespace Share.CircuitFramework.Execution;

/// <summary>
/// 执行层：按拓扑推进节点。复用 Modules.Pipeline：
///   PipelineContext 传数据（节点输出写入 SharedData，键 "$NodeId.$Port"），
///   StagePipeline 做顺序骨架（每节点注册为一个 IPipelineStage<PipelineContext>）。
/// 不重新发明运行编排——pipeline 是执行后端，电路框架是它的上层描述/编译层。
/// 执行期只经编译期索引（<see cref="CircuitRuntimeIndex"/>）查表：节点查找 / 入边绑定 / 入口 / 出口一次装配，
/// 不再逐节点重扫 Wires / EntrySymbol（原 O(N·W) 热点）。
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
        // 编译期索引（节点 / 入边绑定 / 入口 / 出口）：编译产物携带则零重建；手工构造的计划即时构建等价索引。
        var index = plan.Index ?? CircuitRuntimeIndex.Build(circuit);

        if (entryInputs is not null)
        {
            // 入口投影：入口值入上下文（键 "$entry.{Port}"，与节点入端口绑定同源）
            foreach (var entry in index.EntryPorts)
            {
                if (entryInputs.TryGetValue(entry, out var value))
                    ctx.SharedData[$"$entry.{entry}"] = value;
            }
        }

        foreach (var nodeId in plan.TopologicalOrder)
        {
            if (!index.NodesById.TryGetValue(nodeId, out var node))
                throw new InvalidOperationException(
                    $"执行计划引用不存在的节点 '{nodeId}'（编译产物与电路不一致）。");
            var impl = resolveNode(node.Kind);
            stagePipeline.Register(new CircuitNodeStage(node, impl, index.InputsOf(nodeId)));
        }

        // 节点输出写入上下文 SharedData 键 "$NodeId.$Port"
        await stagePipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);

        // 出口投影：Exit 端口名 "NodeId.Port" → 对应节点输出（格式/存在性已由编译期 CFG-015/016 判红）
        var result = new Dictionary<string, object?>();
        foreach (var exit in index.Exits)
        {
            if (ctx.SharedData.TryGetValue($"${exit.Port}", out var value))
                result[exit.Port] = value;
        }
        return result;
    }

    private sealed class CircuitNodeStage : IPipelineStage<PipelineContext>
    {
        private readonly CircuitNode _node;
        private readonly ICircuitNode _impl;
        private readonly IReadOnlyList<InputBinding> _bindings;

        public string Name => _node.Id;
        public string Description => $"circuit node {_node.Id} ({_node.Kind})";
        public bool CanExecute(PipelineContext context) => !context.IsCancelled;

        public CircuitNodeStage(CircuitNode node, ICircuitNode impl, IReadOnlyList<InputBinding> bindings)
        {
            _node = node;
            _impl = impl;
            _bindings = bindings;
        }

        public async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken)
        {
            // 入参按编译期绑定表装配（零逐节点重扫 Wire / EntrySymbol）
            var inputs = new Dictionary<string, object?>(_bindings.Count, StringComparer.Ordinal);
            foreach (var binding in _bindings)
            {
                if (context.SharedData.TryGetValue(binding.SourceKey, out var value))
                    inputs[binding.Port] = value;
            }

            var outputs = await _impl.ExecuteAsync(inputs, cancellationToken).ConfigureAwait(false);
            foreach (var kv in outputs)
                context.SharedData[$"${_node.Id}.{kv.Key}"] = kv.Value;
            context.SetStageResult(_node.Id, true);
        }
    }
}