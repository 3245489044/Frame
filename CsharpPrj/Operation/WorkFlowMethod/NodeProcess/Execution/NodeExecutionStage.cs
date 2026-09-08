#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.Pipeline;
using Share.StateManagement;
using SkeletonFlow;
using CommandFramework.Parameters.Abstractions;
using CommandFramework.Parameters;
using WorkflowExecution.Pipeline;
using WorkFlowMethod.Diagnostics;
using WorkFlowMethod.Execution.Session;
using GraphFramework.Snapshots;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

using SMExecutionStatus = Share.StateManagement.ExecutionStatus;
using WfPipelineContext = WorkflowExecution.Pipeline.PipelineContext;

namespace WorkFlowMethod.Skeletons.NodeProcess.Execution
{
    public class NodeExecutionStage : PipelineStageBase<WfPipelineContext>
    {
        private readonly IExecutionStateStore<SMExecutionStatus> _stateStore;
        private readonly Share.StateManagement.INodeExecutionCoordinator _coordinator;
        private readonly IParameterResolver _parameterResolver;
        private readonly SemaphoreSlim _executionSemaphore = new(10, 10);
        private static readonly TimeSpan StateRetentionPeriod = TimeSpan.FromMinutes(5);
        private bool _disposed;

        public NodeExecutionStage(
            IExecutionStateStore<SMExecutionStatus>? stateStore = null,
            Share.StateManagement.INodeExecutionCoordinator? coordinator = null,
            IParameterResolver? parameterResolver = null)
            : base("NodeExecution", "节点执行状态管理 - 基于 StateManagement 框架")
        {
            _stateStore = stateStore ?? new ExecutionStateStore<SMExecutionStatus>();
            _coordinator = coordinator ?? new NodeExecutionCoordinator(10, _stateStore);
            _parameterResolver = parameterResolver ?? new CommandFramework.Parameters.ParameterResolver();
        }

        protected override async Task ExecuteCoreAsync(
            WfPipelineContext context,
            CancellationToken cancellationToken)
        {
            var wfContext = context;
            var session = wfContext.GetSession<ExecutionSession>();
            if (session == null) return;

            var executionId = session.SessionId;
            StartExecution(executionId);
            wfContext.SetData("ExecutionStage", this);

            var snapshot = wfContext.GetGraphSnapshot<GraphSnapshot<string, NodeConfig, object>>();
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

                WorkFlowMethodLog.Info("NodeExecutionStage",
                    $"执行状态管理已初始化: ExecutionId={executionId}, " +
                    $"Nodes={snapshot.NodeKeys.Count}, " +
                    $"EntryNode={wfContext.CurrentNodeKey}");
            }
            else
            {
                WorkFlowMethodLog.Warn("NodeExecutionStage",
                    $"执行状态管理已初始化，但 GraphSnapshot<string, NodeConfig, object> 为空: ExecutionId={executionId}");
            }

            await Task.CompletedTask;
        }

        #region 执行状态管理 API

        public bool StartExecution(string executionId)
        {
            var state = _stateStore.GetOrCreate(executionId);
            return state.TryStart();
        }

        public bool PauseExecution(string executionId)
        {
            var state = _stateStore.Get(executionId);
            return state?.TryPause() ?? false;
        }

        public bool ResumeExecution(string executionId)
        {
            var state = _stateStore.Get(executionId);
            return state?.TryResume() ?? false;
        }

        public bool TerminateExecution(string executionId)
        {
            var state = _stateStore.Get(executionId);
            if (state == null) return false;

            if (state.TryTerminate())
            {
                _coordinator.CancelAll(executionId);
                CleanupExecutionResources(executionId);
                ScheduleStateRemoval(executionId);
                return true;
            }
            return false;
        }

        #endregion

        #region 节点执行协调

        public bool TryRegisterNodeExecution(string executionId, string nodeKey, out bool isDuplicate)
        {
            return _coordinator.TryRegisterNode(executionId, nodeKey, out isDuplicate);
        }

        public async Task<bool> ExecuteNodeAsync(
            string executionId,
            string nodeKey,
            Func<Task> executeFunc,
            CancellationToken cancellationToken = default)
        {
            return await _coordinator.ExecuteNodeAsync(executionId, nodeKey, executeFunc, cancellationToken);
        }

        public async Task<Share.StateManagement.NodeExecutionResult> ExecuteNodeWithParametersAsync(
            string executionId,
            string nodeKey,
            Share.StateManagement.INodeExecutor executor,
            IDictionary<string, object> parameters,
            CancellationToken cancellationToken = default)
        {
            if (!_coordinator.TryRegisterNode(executionId, nodeKey, out _))
                return new Share.StateManagement.NodeExecutionResult { Success = false };

            try
            {
                await _executionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new Share.StateManagement.NodeExecutionResult { Success = false, Error = new OperationCanceledException("等待信号量时被取消") };
            }

            try
            {
                var state = _stateStore.Get(executionId);
                if (state != null && state.CurrentState == SMExecutionStatus.Terminated)
                    return new Share.StateManagement.NodeExecutionResult { Success = false };

                var mergedParameters = _parameterResolver.MergeParameters(executionId, parameters);
                var result = await executor.ExecuteAsync(nodeKey, mergedParameters, cancellationToken).ConfigureAwait(false);

                if (result.Success)
                {
                    _parameterResolver.StoreNodeResult(executionId, nodeKey, result);
                    _coordinator.MarkNodeCompleted(executionId, nodeKey);
                }
                else
                {
                    _coordinator.MarkNodeFailed(executionId, nodeKey, result.Error ?? new Exception("节点执行失败"));
                }

                return result;
            }
            catch (Exception ex)
            {
                _coordinator.MarkNodeFailed(executionId, nodeKey, ex);
                return new Share.StateManagement.NodeExecutionResult { Success = false, Error = ex };
            }
            finally
            {
                _executionSemaphore.Release();
            }
        }

        public async Task WaitForNodeCompletionAsync(
            string executionId,
            string nodeKey,
            CancellationToken cancellationToken = default)
        {
            await _coordinator.WaitForNodeCompletionAsync(executionId, nodeKey, cancellationToken);
        }

        public void MarkNodeCompleted(string executionId, string nodeKey)
        {
            _coordinator.MarkNodeCompleted(executionId, nodeKey);

            var state = _stateStore.Get(executionId);
            if (state != null && state.IsActive)
            {
                var activeIds = _coordinator.GetActiveExecutionIds();
                if (!activeIds.Contains(executionId))
                {
                    state.TryComplete();
                    ScheduleStateRemoval(executionId);
                }
            }
        }

        public void MarkNodeFailed(string executionId, string nodeKey, Exception error)
        {
            _coordinator.MarkNodeFailed(executionId, nodeKey, error);

            var state = _stateStore.Get(executionId);
            if (state != null && state.IsActive)
            {
                state.TryFault();
                ScheduleStateRemoval(executionId);
            }
        }

        #endregion

        #region 参数管理（委托给 IParameterResolver）

        /// <summary>
        /// 判断某参数键在指定 executionId 分区下是否已存在。
        /// 用于「裸端口先到者胜」语义（E1）：写入裸端口前先探测，避免后续节点覆盖先行节点产出，
        /// 与 ReadView/ResolveInputs 的裸端口合并规则保持一致。
        /// </summary>
        public bool ContainsExecutionParameter(string executionId, string key)
            => _parameterResolver.GetParameters(executionId).ContainsKey(key);

        public void SetExecutionParameter(string executionId, string key, object value)
        {
            _parameterResolver.SetParameter(executionId, key, value);
        }

        public object GetExecutionParameter(string executionId, string key)
        {
            return _parameterResolver.GetParameter(executionId, key);
        }

        public IDictionary<string, object> GetExecutionParameters(string executionId)
        {
            return _parameterResolver.GetParameters(executionId);
        }

        public object GetNodeResult(string executionId, string nodeKey)
        {
            return _parameterResolver.GetNodeResult(executionId, nodeKey);
        }

        #endregion

        #region 状态查询

        public SMExecutionStatus GetExecutionStatus(string executionId)
        {
            var state = _stateStore.Get(executionId);
            return state?.CurrentState ?? SMExecutionStatus.NotStarted;
        }

        public WorkflowExecutionStats? GetExecutionStats(string executionId)
        {
            var state = _stateStore.Get(executionId);
            if (state == null) return null;

            var completedNodes = new List<string>();
            var failedNodes = new List<string>();

            var nodeResults = _parameterResolver.GetAllNodeResults(executionId);
            foreach (var nodeKey in nodeResults.Keys)
            {
                if (_coordinator.IsNodeCompleted(executionId, nodeKey))
                    completedNodes.Add(nodeKey);
                else
                    failedNodes.Add(nodeKey);
            }

            var startTime = state.StartTime;
            var endTime = state.IsFinished ? DateTime.UtcNow : (DateTime?)null;
            var duration = endTime.HasValue ? endTime.Value - startTime : DateTime.UtcNow - startTime;

            return new WorkflowExecutionStats
            {
                ExecutionId = executionId,
                Status = state.CurrentState,
                CurrentNode = _coordinator.GetCurrentNode(executionId),
                CompletedNodes = completedNodes,
                FailedNodes = failedNodes,
                LastError = failedNodes.Count > 0 ? failedNodes.Last() : null,
                StartTime = startTime,
                EndTime = endTime,
                Duration = duration
            };
        }

        public string GetCurrentNode(string executionId)
        {
            return _coordinator.GetCurrentNode(executionId);
        }

        public void SetCurrentNode(string executionId, string nodeKey)
        {
            _coordinator.SetCurrentNode(executionId, nodeKey);
        }

        public IEnumerable<string> GetActiveExecutions()
        {
            return _stateStore.GetActiveIds();
        }

        public IEnumerable<string> GetAllExecutions() => _stateStore.GetAllIds();

        public bool IsNodeCompleted(string executionId, string nodeKey)
        {
            return _coordinator.IsNodeCompleted(executionId, nodeKey);
        }

        #endregion

        #region 内部方法

        private void CleanupExecutionResources(string executionId)
        {
            _parameterResolver.Cleanup(executionId);
        }


        private void ScheduleStateRemoval(string executionId)
        {
            Task.Delay(StateRetentionPeriod).ContinueWith(t =>
            {
                try
                {
                    var state = _stateStore.Get(executionId);
                    if (state != null && state.IsFinished)
                    {
                        _stateStore.Remove(executionId);
                        CleanupExecutionResources(executionId);
                    }
                }
                catch (Exception ex)
                {
                    WorkFlowMethodLog.Log("NodeExecutionStage_ScheduleStateRemoval_failed",
                        new { executionId, error = ex.Message });
                }
            }, TaskScheduler.Default);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var id in _stateStore.GetAllIds())
            {
                _coordinator.CancelAll(id);
            }
            _executionSemaphore.Dispose();
        }

        #endregion
    }
}
