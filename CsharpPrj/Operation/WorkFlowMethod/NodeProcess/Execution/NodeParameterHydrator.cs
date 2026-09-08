#region USINGS
using System.Threading;
using System.Threading.Tasks;
using WorkflowExecution.Pipeline;
using WorkFlowMethod.Execution.Session;
using WorkFlowMethod.Skeletons.NodeProcess.Abstractions;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
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
            INodeProcessOutputStore store,
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

            foreach (var (port, value) in node.OutputPorts)
            {
                if (value is null) continue;
                // 裸端口 与 {nodeKey}.{port} 皆写入，保证同 workflowId 内可溯源。
                // 裸端口「先到者胜」（E1）：仅当未被任一更早节点输出占用时才写入，与 ReadView/ResolveInputs
                // 的裸端口合并规则一致——若先写裸端口，后续节点同名裸端口不覆盖，避免跨节点互覆盖。
                if (!executionStage.ContainsExecutionParameter(executionId, port))
                    executionStage.SetExecutionParameter(executionId, port, value);
                executionStage.SetExecutionParameter(executionId, $"{nodeKey}.{port}", value);
            }
        }
    }
}