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

        public NodeExecutionStepSkeleton(ISkeleton<WfPipelineContext, WfPipelineContext>? nodeProcessor = null)
            : base("NodeExecutionStep", "单步节点执行 - 注册执行、处理节点、标记完成")
        {
            _nodeProcessor = nodeProcessor ?? new NodeChainSkeleton();
        }

        protected override WfPipelineContext ExecuteCore(WfPipelineContext input, SkeletonContext context)
        {
            throw new NotSupportedException(
                $"NodeExecutionStepSkeleton 不支持同步执行。" +
                $"请使用异步路径 ExecuteCoreAsync。");
        }

        protected override async Task<WfPipelineContext> ExecuteCoreAsync(WfPipelineContext input, SkeletonContext context)
        {
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
    }
}
