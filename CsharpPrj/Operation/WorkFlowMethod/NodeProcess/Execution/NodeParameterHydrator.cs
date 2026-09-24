#region USINGS
using System.Threading;
using System.Threading.Tasks;
using Modules.Node.Ports;
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
        public static async Task HydrateCurrentNodeAsync(
            WfPipelineContext context,
            INodeOutputStore store,
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

            // 键空间规则唯一实现 = Modules.Node.Ports.PortKeySpace（与读视图同一实现，E1/G2）：
            // 既有参数键先回放进同一键空间（裸键登记为已占用），再按规则准入本节点端口——
            // 裸端口「先到者胜」（未被任一更早节点占用时才写入，避免跨节点互覆盖）、{node}.{port} 恒有。
            var space = new PortKeySpace<object?>();
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
                executionStage.SetExecutionParameter(executionId, $"{nodeKey}.{port}", value);
            }
        }
    }
}