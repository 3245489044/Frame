
#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using Modules.Pipeline;
using Share.StateManagement;
using Share.StateManagement.Stages;
using SkeletonFlow;
using WorkflowExecution.Pipeline;
using WorkFlowMethod.Diagnostics;
using WorkFlowMethod.Execution.Session;
using WorkFlowMethod.Skeletons.NodeProcess;
#endregion

using WfPipelineContext = WorkflowExecution.Pipeline.PipelineContext;

using SMExecutionStatus = Share.StateManagement.ExecutionStatus;

namespace WorkFlowMethod.Orchestration.State.Stages
{
    public class OrchestrationLoopStage : StageBase
    {
        private const string ExecutionStageKey = "ExecutionStage";

        private readonly ISkeleton<WfPipelineContext, WfPipelineContext>? _nodeProcessor;

        public OrchestrationLoopStage(
            ISkeleton<WfPipelineContext, WfPipelineContext>? nodeProcessor = null,
            string name = "LoopManager",
            string? description = null)
            : base(name, description ?? "循环执行 - 编排层特有")
        {
            _nodeProcessor = nodeProcessor;
        }

        private static ExecutionSession? GetSession(WfPipelineContext? ctx)
            => ctx?.GetSession<ExecutionSession>();

        protected override async Task ExecuteCoreAsync(
            IStageContext context,
            CancellationToken cancellationToken)
        {
            var wfContext = GetPipelineContext(context);
            if (wfContext == null) return;

            var session = GetSession(wfContext);
            if (session == null) return;

            var executionStage = wfContext.GetData<OrchestrationExecutionStage>(ExecutionStageKey);
            if (executionStage == null)
            {
                WorkFlowMethodLog.Warn(Name, "未找到 ExecutionStage，跳过循环执行");
                return;
            }

            var executionId = session.SessionId;
            var status = executionStage.GetExecutionStatus(executionId);

            if (status != SMExecutionStatus.Running)
            {
                WorkFlowMethodLog.Warn(Name, $"执行状态不是 Running（当前: {status}），跳过循环执行");
                return;
            }

            WorkFlowMethodLog.Info(Name, $"开始循环执行，ExecutionId={executionId}");

            var loopPipeline = SkeletonExtensions.Loop<WfPipelineContext>(
                cond: (ctx) =>
                {
                    if (string.IsNullOrEmpty(ctx.CurrentNodeKey))
                        return false;

                    var currentStatus = executionStage.GetExecutionStatus(executionId);
                    return currentStatus == SMExecutionStatus.Running;
                },
                body: _nodeProcessor ?? new NodeChainSkeleton());

            var loopSkelContext = new SkeletonContext();
            loopSkelContext.CancellationToken = cancellationToken;
            await loopPipeline.ExecuteAsync(wfContext, loopSkelContext).ConfigureAwait(false);
            loopSkelContext.Dispose();

            WorkFlowMethodLog.Info(Name, "循环执行完成");
        }

        private static WfPipelineContext? GetPipelineContext(IStageContext context)
            => context as WfPipelineContext;
    }
}
