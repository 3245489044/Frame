#region USINGS
using System.Threading;
using System.Threading.Tasks;
using Modules.Pipeline;
using GraphFramework.Snapshots;
using WorkflowExecution.Pipeline;
using WorkFlowMethod.Skeletons.NodeProcess.Abstractions;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

using WfPipelineContext = WorkflowExecution.Pipeline.PipelineContext;

namespace WorkFlowMethod.Skeletons.NodeProcess.Execution
{
    /// <summary>
    /// 节点即存储单元 —— 节点输出持久化 stage。
    /// 在 NodeLoop 循环体内、单节点执行步之后调用：把当前节点产出的 <see cref="PipelineContext.CurrentOutput"/>
    /// 提取为端口，写入 <see cref="INodeProcessOutputStore"/>，使节点成为下游入参的持久化读源。
    /// 未注入 store（null）→ 直接返回，管线零回归。
    /// </summary>
    public sealed class NodeOutputPersistStage : PipelineStageBase<WfPipelineContext>
    {
        private readonly INodeProcessOutputStore? _store;
        private readonly NodePersistCoordinator? _coordinator;

        public NodeOutputPersistStage(
            INodeProcessOutputStore? store = null,
            NodePersistCoordinator? coordinator = null)
            : base("NodeOutputPersist", "节点即存储单元 - 持久化节点产出端口")
        {
            _store = store;
            _coordinator = coordinator;
        }

        protected override async Task ExecuteCoreAsync(WfPipelineContext context, CancellationToken cancellationToken)
        {
            if (_store is null) return; // 未装配 → 零回归
            await PersistAsync(context, _store, cancellationToken, _coordinator).ConfigureAwait(false);
        }

        /// <summary>
        /// 持久化当前节点产出：{nodeKey} + 端口 → store。
        /// 端口来源：NodeConfig.ResultKeys 定向提取；未命中配置则反射结果公共属性；两者皆空则以节点键为默认端口整存。
        /// 空 CurrentNodeKey 或空 CurrentOutput → 不写；null 端口值跳过。
        /// 注入 coordinator 时，同一 workflowId 的整批端口写经单写门（fail-fast + 有界等待）串行落盘。
        /// </summary>
        public static async Task PersistAsync(
            WfPipelineContext context,
            INodeProcessOutputStore store,
            CancellationToken ct = default,
            NodePersistCoordinator? coordinator = null)
        {
            var nodeKey = context.CurrentNodeKey;
            if (string.IsNullOrEmpty(nodeKey)) return;

            var result = context.CurrentOutput;
            if (result is null) return;

            var snapshot = context.GetGraphSnapshot<GraphSnapshot<string, NodeConfig, object>>();
            var config = snapshot?.GetNodeData(nodeKey);

            var ports = NodeResultExtractor.ExtractResultKeys(config, result);
            if (ports.Count == 0)
                ports[nodeKey] = result; // 无命名端口 → 以节点键为默认输出端口整存

            var workflowId = context.WorkflowId;

            async Task WriteCore()
            {
                foreach (var (port, value) in ports)
                {
                    if (value is null) continue; // 空端口不产生槽位
                    await store.WriteOutputAsync(workflowId, nodeKey, port, value, ct).ConfigureAwait(false);
                }
            }

            if (coordinator is not null)
                await coordinator.RunWriteAsync(workflowId, _ => WriteCore(), ct).ConfigureAwait(false);
            else
                await WriteCore().ConfigureAwait(false);
        }
    }
}