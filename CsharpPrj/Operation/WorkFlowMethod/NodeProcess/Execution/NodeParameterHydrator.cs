#region USINGS
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GraphFramework.Snapshots;
using Modules.KeySpace.Model;
using Modules.Node.Identity;
using Modules.Node.Ports;
using WorkFlowMethod.Diagnostics;
using WorkFlowMethod.KeySpace.Adapters;
using WorkflowExecution.Pipeline;
using WorkFlowMethod.Execution.Session;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using Operation.WorkFlowMethod.Nodes.Contracts;
#endregion

using WfPipelineContext = WorkflowExecution.Pipeline.PipelineContext;

namespace WorkFlowMethod.Skeletons.NodeProcess.Execution
{
    /// <summary>
    /// 入参读源水化 —— 把已持久化的节点输出端口写入 IParameterResolver（经 NodeExecutionStage.SetExecutionParameter），
    /// 使下游节点经 MergeParameters 即可按 {port} 或 {nodeKey}.{port} 取用节点产出（节点端口成为入参源）。
    /// 依赖的 KeyMapping/KeyProcessor 键处理仍属构建期职责；本助手只负责运行时把端口推进参数解析器。
    /// 缺执行上下文（未注册 NodeExecutionStage / 无执行会话）→ 不写，零回归。
    /// </summary>
    public static class NodeParameterHydrator
    {
        public static Task HydrateCurrentNodeAsync(
            WfPipelineContext context,
            INodeOutputStore store,
            CancellationToken ct = default)
            => HydrateCurrentNodeAsync(context, store, contracts: null, ct: ct);

        /// <summary>契约驱动水化：Required 输出缺失 ⇒ 诊断并中止本节点水化（不写半套参数）。</summary>
        public static async Task HydrateCurrentNodeAsync(
            WfPipelineContext context,
            INodeOutputStore store,
            IReadOnlyList<PortContract>? contracts,
            CancellationToken ct = default)
        {
            var nodeKey = context.CurrentNodeKey;
            if (string.IsNullOrEmpty(nodeKey)) return;

            var executionStage = context.GetData<NodeExecutionStage>("ExecutionStage");
            if (executionStage is null) return;

            // 以 workflowId 作为参数解析器分区键：与持久化存储键、下游读取键一致，
            // 不依赖 ExecutionSession 具体类型（Context.WorkflowId 经 GetSessionId 解析）。
            var executionId = context.WorkflowId;
            if (string.IsNullOrEmpty(executionId)) return;

            var node = await store.TryGetNodeOutputAsync(executionId, nodeKey, ct).ConfigureAwait(false);
            if (node is null) return;

            // 未显式传契约 ⇒ 从图快照的当前 NodeConfig 投影（声明面单源）。
            var snapshot = context.GetGraphSnapshot<GraphSnapshot<string, NodeConfig, object>>();
            var effectiveContracts = contracts ?? NodePortContractRegistry.ForNode(snapshot?.GetNodeData(nodeKey));
            if (effectiveContracts is { Count: > 0 })
            {
                // A5：绑定唯一入口 = KeySpaceFacade.BindOut（不再每轮 new KeyBinder）。
                var binding = WorkflowKeySpaceDefaults.CreateFacade().BindOut(effectiveContracts, node.OutputPorts);
                if (binding.RequiredMissing)
                {
                    WorkFlowMethodLog.Warn(
                        "NodePortHydrator",
                        $"port.required-missing: 节点 '{nodeKey}' 的 Required 输出缺失，中止入参水化", nodeKey);
                    return;
                }
            }

            // 键空间规则唯一实现 = Modules.Node.Ports.PortKeySpace（与读视图同一实现，E1/G2）：
            // A5：实例由执行会话持有（每 workflow 一个），既有参数键只做幂等回放，不再每轮重建。
            var space = executionStage.GetOrCreatePortSpace(executionId);
            foreach (var existing in executionStage.GetExecutionParameters(executionId).Keys)
            {
                space.Adopt(existing);
            }

            foreach (var (port, value) in node.OutputPorts)
            {
                if (value is null) continue; // 空端口不合入（与框架侧同一规则）

                if (space.TryAddBare(nodeKey, port, value))
                {
                    executionStage.SetExecutionParameter(executionId, port, value);
                }

                space.TryAdd(nodeKey, port, value);
                executionStage.SetExecutionParameter(executionId, NodePortKeys.Format(nodeKey, port), value);
            }
        }
    }
}