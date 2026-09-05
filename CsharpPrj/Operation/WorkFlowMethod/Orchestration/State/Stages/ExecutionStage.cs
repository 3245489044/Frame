
#region USINGS
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.Pipeline;
using Modules.StateMachine;
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
    public class OrchestrationExecutionStage : ExecutionStage
    {
        public OrchestrationExecutionStage(
            IStateMachineStore<SMExecutionStatus>? stateStore = null,
            INodeExecutionCoordinator? coordinator = null)
            : base(
                  coordinator: coordinator,
                  stateStore: stateStore,
                  name: "ExecutionManager",
                  description: "执行状态管理 - 基于 Share.ExecutionStage")
        {
        }

        private static ExecutionSession? GetSession(WfPipelineContext? ctx)
            => ctx?.GetSession<ExecutionSession>();

        protected override async Task ExecuteCoreAsync(
            IStageContext context,
            CancellationToken cancellationToken)
        {
            var wfContext = context as WfPipelineContext;
            if (wfContext == null) return;

            var session = GetSession(wfContext);
            if (session == null) return;

            var executionId = session.SessionId;
            StartExecution(executionId);
            wfContext.SetData("ExecutionStage", this);

            var snapshot = wfContext.GetGraphSnapshot<global::GraphFramework.Snapshots.GraphSnapshot<string, NodeConfig, object>>();
            if (snapshot != null && snapshot.NodeKeys.Count > 0)
            {
                foreach (var nodeKey in snapshot.NodeKeys)
                {
                    TryRegisterNodeExecution(executionId, nodeKey, out _);
                }

                if (string.IsNullOrEmpty(wfContext.CurrentNodeKey))
                {
                    wfContext.CurrentNodeKey = snapshot.NodeKeys[0];
                }

                WorkFlowMethodLog.Info(Name,
                    $"执行状态管理已初始化: ExecutionId={executionId}, " +
                    $"Nodes={snapshot.NodeKeys.Count}, " +
                    $"EntryNode={wfContext.CurrentNodeKey}");
            }
            else
            {
                WorkFlowMethodLog.Warn(Name,
                    $"执行状态管理已初始化，但 GraphSnapshot 为空: ExecutionId={executionId}");
            }

            await Task.CompletedTask;
        }

    }
}
