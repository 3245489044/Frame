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
            // G3 受控边门控 / G4 容错策略：编译期索引一次查表（缺失 = 无条件 / 无策略）
            var policy = index.FailurePolicyOf(nodeId);
            var altNodeId = policy.FallbackNodeId ?? policy.CatchRouteNodeId;
            var altKind = altNodeId is not null && index.NodesById.TryGetValue(altNodeId, out var altNode)
                ? altNode.Kind
                : null;
            stagePipeline.Register(new CircuitNodeStage(
                node, impl, index.InputsOf(nodeId), index.RoutesOf(nodeId), policy, altKind, resolveNode));
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
        private readonly IReadOnlyList<RouteBinding> _gates;
        private readonly NodeFailurePolicy _policy;
        private readonly string? _altKind;
        private readonly Func<string, ICircuitNode> _resolveNode;

        public string Name => _node.Id;
        public string Description => $"circuit node {_node.Id} ({_node.Kind})";

        public CircuitNodeStage(
            CircuitNode node,
            ICircuitNode impl,
            IReadOnlyList<InputBinding> bindings,
            IReadOnlyList<RouteBinding> gates,
            NodeFailurePolicy policy,
            string? altKind,
            Func<string, ICircuitNode> resolveNode)
        {
            _node = node;
            _impl = impl;
            _bindings = bindings;
            _gates = gates;
            _policy = policy;
            _altKind = altKind;
            _resolveNode = resolveNode;
        }

        /// <summary>受控边门控（G3）：全部判定键为真才执行（无门控 = 无条件）。执行时刻对 SharedData 求判定。</summary>
        public bool CanExecute(PipelineContext context)
            => !context.IsCancelled && _gates.All(g => JudgeHolds(context.SharedData, g.WhenKey));

        public async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken)
        {
            try
            {
                var outputs = await RunWithPolicyAsync(context, cancellationToken).ConfigureAwait(false);
                if (outputs is not null)
                {
                    foreach (var kv in outputs)
                        context.SharedData[$"${_node.Id}.{kv.Key}"] = kv.Value;
                }
                context.SetStageResult(_node.Id, true);
            }
            catch (Exception ex) when (_policy.Guarded && ex is not OperationCanceledException)
            {
                // TryGuard（G4）：受保护节点失败 → 吞并继续（不产输出）。
                // 置成功位：StagePipeline 对「非成功」阶段 fail-fast 短路，故受保护失败须显式标记为「已收敛」以续行。
                context.SetStageResult(_node.Id, true);
            }
        }

        /// <summary>入参按编译期绑定表装配（零逐节点重扫 Wire / EntrySymbol / 数据流符号）。</summary>
        private Dictionary<string, object?> BuildInputs(PipelineContext context)
        {
            var inputs = new Dictionary<string, object?>(_bindings.Count, StringComparer.Ordinal);
            foreach (var binding in _bindings)
            {
                if (DataflowValueResolver.TryResolve(context.SharedData, binding, out var value))
                    inputs[binding.Port] = value;
            }
            return inputs;
        }

        /// <summary>重试（G4 机制，指数退避）+ 失败路由（Fallback/CatchRoute）后执行节点体。</summary>
        private async Task<IReadOnlyDictionary<string, object?>?> RunWithPolicyAsync(
            PipelineContext context, CancellationToken cancellationToken)
        {
            var inputs = BuildInputs(context);
            var attempts = Math.Max(1, _policy.RetryMaxAttempts);
            Exception? last = null;

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    return await _impl.ExecuteAsync(inputs, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw; // 取消原样上抛，不归为失败、不重试
                }
                catch (Exception ex)
                {
                    last = ex;
                    if (attempt < attempts && _policy.RetryBackoffBaseMs > 0)
                        await Task.Delay(
                                TimeSpan.FromMilliseconds(_policy.RetryBackoffBaseMs * Math.Pow(2, attempt - 1)),
                                cancellationToken)
                            .ConfigureAwait(false);
                }
            }

            // 失败路由：主路径用尽 → 备选/补救节点（同入参），产物仍以本节点 Id 写入上下文（下游无感）。
            if (_altKind is not null)
                return await _resolveNode(_altKind).ExecuteAsync(inputs, cancellationToken).ConfigureAwait(false);

            throw last!;
        }

        /// <summary>判定键取值（结构化布尔事实；缺键/假 ⇒ 不激活）。</summary>
        private static bool JudgeHolds(IDictionary<string, object?> data, string key)
        {
            if (!data.TryGetValue(key, out var value)) return false;
            return value switch
            {
                null => false,
                bool b => b,
                string s => bool.TryParse(s, out var parsed) ? parsed : !string.IsNullOrEmpty(s),
                _ => true,
            };
        }
    }
}