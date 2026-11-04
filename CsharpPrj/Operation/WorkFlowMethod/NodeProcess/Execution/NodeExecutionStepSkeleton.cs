#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using SkeletonFlow;
using WorkflowExecution.Pipeline;
using WorkFlowMethod.Execution.Session;
using GraphFramework.Snapshots;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
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
            var nodeKey = input.CurrentNodeKey;

            try
            {
                var result = await _nodeProcessor.ExecuteAsync(input, context).ConfigureAwait(false);

                var skipped = IsSkipped(context);
                if (session != null && !string.IsNullOrEmpty(nodeKey))
                {
                    if (skipped)
                        session.MarkNodeSkipped(nodeKey, GetSkipReason(context));
                    else
                        MarkSessionCompleted(session, nodeKey);
                }

                if (!skipped && executionStage != null && executionId != null && !string.IsNullOrEmpty(nodeKey))
                    executionStage.MarkNodeCompleted(executionId, nodeKey);

                AdvanceToNextNode(result);
                return result;
            }
            catch (Exception ex)
            {
                if (session != null && !string.IsNullOrEmpty(nodeKey))
                    MarkSessionFailed(session, nodeKey, ex.Message);

                if (executionStage != null && executionId != null && !string.IsNullOrEmpty(nodeKey))
                    executionStage.MarkNodeFailed(executionId, nodeKey, ex);

                throw;
            }
        }

        private async Task ExecuteWithExecutorAsync(WfPipelineContext input, SkeletonContext context)
        {
            var session = input.GetSession<ExecutionSession>();
            var executionStage = input.GetData<NodeExecutionStage>("ExecutionStage");
            var executionId = session?.SessionId;
            var nodeKey = input.CurrentNodeKey;

            if (executionStage == null || executionId == null || string.IsNullOrEmpty(nodeKey))
            {
                return;
            }

            var parameters = executionStage.GetExecutionParameters(executionId);
            var result = await executionStage.ExecuteNodeWithParametersAsync(
                executionId,
                nodeKey,
                _executor!,
                parameters,
                context.CancellationToken).ConfigureAwait(false);

            if (session != null)
            {
                if (result.Success)
                    MarkSessionCompleted(session, nodeKey);
                else
                    MarkSessionFailed(session, nodeKey, result.Error?.Message ?? "节点执行失败");
            }

            if (result.Success)
            {
                AdvanceToNextNode(input);
            }
            else
            {
                // 失败停机：循环 predicate 读到该标记后终止，避免在同一节点上空转。
                input.SetData("NodeLoop.StopRequested", true);
            }
        }

        private static bool IsSkipped(SkeletonContext context)
            => context.Items.TryGetValue("NodeChainSkeleton.Status", out var status)
               && string.Equals(status as string, "Skipped", StringComparison.OrdinalIgnoreCase);

        private static string? GetSkipReason(SkeletonContext context)
            => context.Items.TryGetValue("NodeChainSkeleton.Warning", out var warning)
                ? warning as string
                : null;

        private static void MarkSessionCompleted(ExecutionSession session, string nodeKey)
        {
            session.GetOrCreateNodeState(nodeKey).Start();
            session.MarkNodeCompleted(nodeKey);
        }

        private static void MarkSessionFailed(ExecutionSession session, string nodeKey, string error)
        {
            session.GetOrCreateNodeState(nodeKey).Start();
            session.MarkNodeFailed(nodeKey, error);
        }

        /// <summary>
        /// 执行成功后推进游标：当前节点 → 快照后继节点的首个，并累计 loop 轮次。
        /// </summary>
        private static void AdvanceToNextNode(WfPipelineContext input)
        {
            var nodeKey = input.CurrentNodeKey;
            if (!string.IsNullOrEmpty(nodeKey))
            {
                var snapshot = input.GetGraphSnapshot<GraphSnapshot<string, NodeConfig, object>>();
                var nextKeys = snapshot?.GetNextNodeKeys(nodeKey);
                input.NextNodeKey = nextKeys is { Count: > 0 } ? nextKeys[0] : null;
                input.CurrentNodeKey = input.NextNodeKey;
            }

            var iteration = input.GetData<int>("NodeLoop.IterationCount");
            input.SetData("NodeLoop.IterationCount", iteration + 1);
        }
    }
}
