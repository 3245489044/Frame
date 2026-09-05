
#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.Pipeline;
using Share.StateManagement;
using Share.StateManagement.Stages;
using WorkflowExecution.Pipeline;
using WorkFlowMethod.Diagnostics;
using WorkFlowMethod.Execution.Session;
using GraphFramework.Snapshots;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

using WfPipelineContext = WorkflowExecution.Pipeline.PipelineContext;

using SMExecutionStatus = Share.StateManagement.ExecutionStatus;

namespace WorkFlowMethod.Orchestration.State.Stages
{
    public class OrchestrationErrorHandlingStage : ErrorHandlingStage
    {
        private const string ExecutionStageKey = "ExecutionStage";

        public OrchestrationErrorHandlingStage(
            string name = "ErrorHandler",
            string? description = null)
            : base(
                  handlerRegistry: null,
                  name: name,
                  description: description ?? "错误处理 - 编排层特有")
        {
        }

        private static ExecutionSession? GetSession(WfPipelineContext? ctx)
            => ctx?.GetSession<ExecutionSession>();

        protected override bool IsNodeFailed(IStageContext context, string nodeKey)
        {
            var wfContext = GetPipelineContext(context);
            if (wfContext == null) return base.IsNodeFailed(context, nodeKey);

            var session = GetSession(wfContext);
            if (session == null) return base.IsNodeFailed(context, nodeKey);

            var nodeState = session.GetNodeState(nodeKey);
            if (nodeState != null && nodeState.Status == NodeStatus.Failed)
                return true;

            return base.IsNodeFailed(context, nodeKey);
        }

        protected override string GetNodeErrorMessage(IStageContext context, string nodeKey)
        {
            var wfContext = GetPipelineContext(context);
            if (wfContext == null) return base.GetNodeErrorMessage(context, nodeKey);

            var session = GetSession(wfContext);
            if (session == null) return base.GetNodeErrorMessage(context, nodeKey);

            var nodeState = session.GetNodeState(nodeKey);
            if (nodeState != null && !string.IsNullOrEmpty(nodeState.ErrorMessage))
                return nodeState.ErrorMessage;

            return base.GetNodeErrorMessage(context, nodeKey);
        }

        protected override async Task ExecuteCoreAsync(
            IStageContext context,
            CancellationToken cancellationToken)
        {
            var wfContext = GetPipelineContext(context);
            if (wfContext == null)
            {
                await base.ExecuteCoreAsync(context, cancellationToken);
                return;
            }

            var session = GetSession(wfContext);
            if (session == null)
            {
                await base.ExecuteCoreAsync(context, cancellationToken);
                return;
            }

            var executionStage = wfContext.GetData<OrchestrationExecutionStage>(ExecutionStageKey);
            if (executionStage == null)
            {
                WorkFlowMethodLog.Warn(Name, "未找到 ExecutionStage，使用基类错误处理");
                await base.ExecuteCoreAsync(context, cancellationToken);
                return;
            }

            var executionId = session.SessionId;
            var status = executionStage.GetExecutionStatus(executionId);

            if (status != SMExecutionStatus.Faulted)
            {
                WorkFlowMethodLog.Debug(Name, $"执行状态不是 Faulted（当前: {status}），跳过错误处理");
                return;
            }

            WorkFlowMethodLog.Info(Name, $"开始错误处理，ExecutionId={executionId}");

            var failedNodes = new List<string>();
            var snapshot = wfContext.GetGraphSnapshot<global::GraphFramework.Snapshots.GraphSnapshot<string, NodeConfig, object>>();
            if (snapshot != null)
            {
                foreach (var nodeKey in snapshot.NodeKeys)
                {
                    if (executionStage.IsNodeCompleted(executionId, nodeKey))
                        continue;

                    if (IsNodeFailed(context, nodeKey))
                    {
                        failedNodes.Add(nodeKey);
                    }
                }
            }

            if (failedNodes.Count > 0)
            {
                WorkFlowMethodLog.Error(Name,
                    $"发现 {failedNodes.Count} 个失败节点: {string.Join(", ", failedNodes)}");

                wfContext.SetData("FailedNodes", failedNodes);
                wfContext.SetData("ErrorHandlingCompleted", true);
            }
            else
            {
                WorkFlowMethodLog.Info(Name, "未发现失败节点");
            }

            await Task.CompletedTask;
        }

        private static WfPipelineContext? GetPipelineContext(IStageContext context)
            => context as WfPipelineContext;
    }
}
