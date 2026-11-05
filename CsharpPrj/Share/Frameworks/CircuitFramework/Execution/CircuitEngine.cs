using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BasicMethod.Block;
using Modules.Pipeline;
using Share.CircuitFramework.Compiler;
using Share.CircuitFramework.Core;

namespace Share.CircuitFramework.Execution;

/// <summary>
/// 执行层：按拓扑推进节点。执行体唯一走 <see cref="BlockPipeline"/>/<see cref="BlockRunner"/>：
///   节点 = 一个 <see cref="IBlock"/>，失败策略（重试/退避/兜底/Guard）编译期产出 <see cref="BlockPolicy"/>，
///   运行期唯一消费方 = BlockRunner。Circuit 只保留描述/编译职责。
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
        CancellationToken cancellationToken = default)
    {
        var index = plan.Index ?? CircuitRuntimeIndex.Build(circuit);
        var context = new BlockContext(cancellationToken);

        if (entryInputs is not null)
        {
            foreach (var entry in index.EntryPorts)
            {
                if (entryInputs.TryGetValue(entry, out var value))
                    context.Data[$"$entry.{entry}"] = value;
            }
        }

        var pipeline = new BlockPipeline(circuit.Id);

        foreach (var nodeId in plan.TopologicalOrder)
        {
            if (!index.NodesById.TryGetValue(nodeId, out var node))
            {
                throw new InvalidOperationException(
                    $"执行计划引用不存在的节点 '{nodeId}'（编译产物与电路不一致）。");
            }

            var impl = resolveNode(node.Kind);
            var policy = index.FailurePolicyOf(nodeId);
            var altNodeId = policy.FallbackNodeId ?? policy.CatchRouteNodeId;
            var altKind = altNodeId is not null
                          && index.NodesById.TryGetValue(altNodeId, out var altNode)
                ? altNode.Kind
                : null;

            pipeline.Add(new CircuitNodeBlock(
                node, impl, index.InputsOf(nodeId), index.RoutesOf(nodeId),
                policy, altKind, resolveNode));
        }

        var run = await pipeline.RunAsync(context, cancellationToken).ConfigureAwait(false);

        if (run.Outcome == BlockRunOutcome.Aborted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new OperationCanceledException($"电路 '{circuit.Id}' 执行被取消。");
        }

        if (run.Outcome == BlockRunOutcome.Failed)
        {
            var fatal = run.Diagnostics.FirstOrDefault(
                d => d.Outcome == BlockOutcome.Failed
                     && d.Fatal
                     && !d.Converged
                     && d.Exception is not null);

            if (fatal?.Exception is not null)
                throw fatal.Exception;
        }

        var result = new Dictionary<string, object?>();
        foreach (var exit in index.Exits)
        {
            if (context.Data.TryGetValue($"${exit.Port}", out var value))
                result[exit.Port] = value;
        }

        return result;
    }

    private sealed class CircuitNodeBlock : IBlock
    {
        private readonly CircuitNode _node;
        private readonly ICircuitNode _impl;
        private readonly IReadOnlyList<InputBinding> _bindings;
        private readonly IReadOnlyList<RouteBinding> _gates;
        private readonly NodeFailurePolicy _policy;
        private readonly string? _altKind;
        private readonly Func<string, ICircuitNode> _resolveNode;

        public CircuitNodeBlock(
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
            Policy = BuildPolicy();
        }

        public string Name => _node.Id;

        public string Description => $"circuit node {_node.Id} ({_node.Kind})";

        public BlockPolicy Policy { get; }

        private BlockPolicy BuildPolicy() => new()
        {
            IsFatal = true,
            CanExecute = context => !context.IsAborted
                                    && _gates.All(gate => GateHolds(context.Data, gate)),
            MaxRetries = Math.Max(0, _policy.RetryMaxAttempts - 1),
            RetryDelay = TimeSpan.FromMilliseconds(Math.Max(0, _policy.RetryBackoffBaseMs)),
            RetryBackoff = BlockRetryBackoff.Exponential,
            MaxRetryDelay = TimeSpan.FromHours(1),
            Guarded = _policy.Guarded,
            FallbackAsync = _altKind is null ? null : RunFallbackAsync
        };

        public async Task<BlockResult> ExecuteAsync(
            BlockContext context,
            CancellationToken cancellationToken = default)
        {
            var inputs = BuildInputs(context);

            try
            {
                var outputs = await _impl.ExecuteAsync(inputs, cancellationToken).ConfigureAwait(false);
                WriteOutputs(context, outputs);
                return BlockResult.Ok();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return BlockResult.Abort($"节点 '{_node.Id}' 执行被取消。");
            }
            catch (Exception ex)
            {
                return BlockResult.Fail(ex.Message, ex);
            }
        }

        private async Task<BlockResult> RunFallbackAsync(
            BlockContext context,
            BlockResult last,
            CancellationToken cancellationToken)
        {
            if (_altKind is null)
                return last;

            var inputs = BuildInputs(context);
            var outputs = await _resolveNode(_altKind)
                .ExecuteAsync(inputs, cancellationToken)
                .ConfigureAwait(false);

            WriteOutputs(context, outputs);
            return BlockResult.Ok($"失败兜底已执行（{last.Message}）");
        }

        private Dictionary<string, object?> BuildInputs(BlockContext context)
        {
            var inputs = new Dictionary<string, object?>(_bindings.Count, StringComparer.Ordinal);
            foreach (var binding in _bindings)
            {
                if (DataflowValueResolver.TryResolve(context.Data, binding, out var value))
                    inputs[binding.Port] = value;
            }
            return inputs;
        }

        private void WriteOutputs(
            BlockContext context,
            IReadOnlyDictionary<string, object?>? outputs)
        {
            if (outputs is null)
                return;

            foreach (var kv in outputs)
                context.Data[$"${_node.Id}.{kv.Key}"] = kv.Value;
        }

        /// <summary>单条路由条件求值：Truthy/Falsy 布尔判定；AnyOf/NoneOf 集合匹配。</summary>
        private static bool GateHolds(IDictionary<string, object?> data, RouteBinding gate)
            => gate.Match switch
            {
                RouteMatch.Truthy => JudgeHolds(data, gate.WhenKey),
                RouteMatch.Falsy => !JudgeHolds(data, gate.WhenKey),
                RouteMatch.AnyOf => ValueInSet(data, gate.WhenKey, gate.MatchValues, wantMatch: true),
                RouteMatch.NoneOf => ValueInSet(data, gate.WhenKey, gate.MatchValues, wantMatch: false),
                _ => JudgeHolds(data, gate.WhenKey),
            };

        private static bool ValueInSet(
            IDictionary<string, object?> data,
            string key,
            IReadOnlyList<string>? values,
            bool wantMatch)
        {
            var actual = data.TryGetValue(key, out var value) ? value?.ToString() : null;
            var hit = false;
            if (actual is not null && values is not null)
            {
                foreach (var candidate in values)
                {
                    if (string.Equals(candidate, actual, StringComparison.Ordinal))
                    {
                        hit = true;
                        break;
                    }
                }
            }
            return wantMatch ? hit : !hit;
        }

        private static bool JudgeHolds(IDictionary<string, object?> data, string key)
        {
            if (!data.TryGetValue(key, out var value))
                return false;

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

