#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using SkeletonFlow;
using WorkflowExecution.Pipeline;
using WorkFlowMethod.Execution.Session;
using GraphFramework.Snapshots;
#endregion

using WfPipelineContext = WorkflowExecution.Pipeline.PipelineContext;

using SMExecutionStatus = Share.StateManagement.ExecutionStatus;

namespace WorkFlowMethod.Skeletons.NodeProcess.Execution
{
    public class NodeExecutionStepSkeleton : SkeletonBase<WfPipelineContext, WfPipelineContext>
    {
        private readonly ISkeleton<WfPipelineContext, WfPipelineContext> _nodeProcessor;
        private readonly Share.StateManagement.INodeExecutor? _executor;

        public NodeExecutionStepSkeleton(
            ISkeleton<WfPipelineContext, WfPipelineContext>? nodeProcessor = null,
            Share.StateManagement.INodeExecutor? executor = null)
            : base("NodeExecutionStep", "单步节点执行 - 注册执行、处理节点、标记完成")
        {
            _nodeProcessor = nodeProcessor ?? new NodeChainSkeleton();
            _executor = executor;
        }

        protected override WfPipelineContext ExecuteCore(WfPipelineContext input, SkeletonContext context)
        {
            throw new NotSupportedException(
                $"NodeExecutionStepSkeleton 不支持同步执行。" +
                $"请使用异步路径 ExecuteCoreAsync。");
        }

        protected override async Task<WfPipelineContext> ExecuteCoreAsync(WfPipelineContext input, SkeletonContext context)
        {
            if (_executor != null)
            {
                await ExecuteWithExecutorAsync(input, context).ConfigureAwait(false);
                return input;
            }

            var session = input.GetSession<ExecutionSession>();
            var executionStage = input.GetData<NodeExecutionStage>("ExecutionStage");
            var executionId = session?.SessionId;


            var result = await _nodeProcessor.ExecuteAsync(input, context).ConfigureAwait(false);

            if (executionStage != null && executionId != null && !string.IsNullOrEmpty(input.CurrentNodeKey))
            {
                executionStage.MarkNodeCompleted(executionId, input.CurrentNodeKey);
            }

            return result;
        }

        private async Task ExecuteWithExecutorAsync(WfPipelineContext input, SkeletonContext context)
        {
            var session = input.GetSession<ExecutionSession>();
            var executionStage = input.GetData<NodeExecutionStage>("ExecutionStage");
            var executionId = session?.SessionId;

            if (executionStage == null || executionId == null || string.IsNullOrEmpty(input.CurrentNodeKey))
            {
                return;
            }

            var parameters = executionStage.GetExecutionParameters(executionId);
            await executionStage.ExecuteNodeWithParametersAsync(
                executionId,
                input.CurrentNodeKey,
                _executor!,
                parameters,
                context.CancellationToken).ConfigureAwait(false);
        }
    }
}
