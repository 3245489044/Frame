using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Nodes;
using Operation.HierarchyMethod.Nodes.Execution;
using Operation.HierarchyMethod.Nodes.Workflows;
using ParameterResolver.KeyMapping;
using Share.CircuitFramework.Core;

namespace Operation.HierarchyMethod.Nodes.Circuit
{
    /// <summary>
    /// 轮步骤电路节点：把既有步骤执行语义（INodeExecutor.ExecuteStepAsync + INodeExecutorHooks.OnAfterStepAsync 横切
    /// + OnStepErrorAsync 错误强类型归因）封入 ICircuitNode 黑盒体。数据读写保持在 ctx（Vars/ur_nodes）侧原语义
    /// （KeyMapping 解析不受电路数据通道影响）；电路侧输出 ResultKeys 供 Wire/Exit 消费。
    /// 取消原样上抛；步骤错误归因（ErrorStep = 出错步骤自身，较编排器的「上一成功步骤」更精确）后上抛，
    /// 由后端继续裁决/harvest（对齐编排器错误语义）。
    /// </summary>
    public sealed class RoundStepCircuitNode : ICircuitNode
    {
        private readonly RoundStep _step;
        private readonly INodeExecutor _executor;
        private readonly INodeExecutorHooks _hooks;
        private readonly NodeRoundContext _ctx;
        private readonly KeyMappingConfig? _globalMapping;

        public RoundStepCircuitNode(RoundStep step, INodeExecutor executor, INodeExecutorHooks hooks,
            NodeRoundContext ctx, KeyMappingConfig? globalMapping)
        {
            _step = step ?? throw new ArgumentNullException(nameof(step));
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _globalMapping = globalMapping;
        }

        public string NodeType => nameof(RoundStepCircuitNode);

        public async Task<IReadOnlyDictionary<string, object?>> ExecuteAsync(
            IReadOnlyDictionary<string, object?> inputs, CancellationToken cancellationToken)
        {
            try
            {
                var record = await _executor.ExecuteStepAsync(_step, _ctx, _globalMapping, cancellationToken)
                    .ConfigureAwait(false);
                if (record is null)
                    return _step.ResultKeys.ToDictionary(k => k, _ => (object?)null, StringComparer.Ordinal);

                await _hooks.OnAfterStepAsync(_ctx, record, cancellationToken).ConfigureAwait(false);

                var outputs = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var rk in _step.ResultKeys)
                    outputs[rk] = record.OutputPorts.TryGetValue(rk, out var v) ? v : null;
                return outputs;
            }
            catch (OperationCanceledException)
            {
                throw; // 取消语义与编排器一致：原样上抛，不归为步骤错误
            }
            catch (Exception ex)
            {
                var synthetic = new NodeExecutionRecord(_step.EffectiveNodeId, "",
                    new Dictionary<string, object?>(StringComparer.Ordinal),
                    new Dictionary<string, (string Node, string Port)>(StringComparer.Ordinal));
                await _hooks.OnStepErrorAsync(_ctx, synthetic, ex, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
    }
}
