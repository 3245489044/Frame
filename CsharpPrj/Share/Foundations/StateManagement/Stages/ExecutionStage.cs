
using Modules.StateMachine;
using Share.StateManagement;

namespace Share.StateManagement.Stages
{
    public class ExecutionStage : StageBase
    {
        private readonly INodeExecutionCoordinator _coordinator;
        private readonly IStateMachineStore<ExecutionStatus> _stateStore;
        private readonly IExecutionParameterStore _parameterStore;
        private static readonly TimeSpan StateRetentionPeriod = TimeSpan.FromMinutes(5);

        public ExecutionStage(
            INodeExecutionCoordinator coordinator,
            IStateMachineStore<ExecutionStatus> stateStore,
            IExecutionParameterStore parameterStore,
            string name = "Execution",
            string? description = null)
            : base(name, description ?? "通用节点执行协调 - 支持并发控制、状态管理、参数管理")
        {
            // 必填端口（T）：执行协调/状态/参数端口由装配点显式提供；禁 ?? new 默认冒充装配。
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
            _parameterStore = parameterStore ?? throw new ArgumentNullException(nameof(parameterStore));
        }

        protected override async Task ExecuteCoreAsync(
            IStageContext context,
            CancellationToken cancellationToken)
        {
            var executionId = context.SessionId ?? Guid.NewGuid().ToString("N");
            var nodeKeys = context.NodeKeys;

            if (nodeKeys == null || nodeKeys.Count == 0)
            {
                LogWarn(context, "没有节点需要执行");
                return;
            }

            StartExecution(executionId);

            LogInfo(context, $"开始执行节点: {nodeKeys.Count} 个节点");

            foreach (var nodeKey in nodeKeys)
            {
                _coordinator.TryRegisterNode(executionId, nodeKey, out _);
            }

            if (string.IsNullOrEmpty(context.CurrentNodeKey) && nodeKeys.Count > 0)
            {
                context.CurrentNodeKey = nodeKeys[0];
            }

            foreach (var nodeKey in nodeKeys)
            {
                cancellationToken.ThrowIfCancellationRequested();

                context.CurrentNodeKey = nodeKey;

                var success = await _coordinator.ExecuteNodeAsync(
                    executionId,
                    nodeKey,
                    async () =>
                    {
                        await Task.CompletedTask;
                    },
                    cancellationToken);

                if (success)
                {
                    LogDebug(context, $"节点执行完成: {nodeKey}");
                }
                else
                {
                    LogWarn(context, $"节点执行失败: {nodeKey}");
                }
            }

            LogInfo(context, $"节点执行完成: {nodeKeys.Count} 个节点");
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

        public bool PauseAll()
        {
            bool result = false;
            foreach (var id in _stateStore.GetActiveIds())
                result |= PauseExecution(id);
            return result;
        }

        public bool ResumeAll()
        {
            bool result = false;
            foreach (var id in _stateStore.GetAllIds())
            {
                var state = _stateStore.Get(id);
                if (state != null && state.CurrentState == ExecutionStatus.Paused)
                    result |= ResumeExecution(id);
            }
            return result;
        }

        public bool TerminateAll()
        {
            bool result = false;
            foreach (var id in _stateStore.GetAllIds())
                result |= TerminateExecution(id);
            return result;
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

        public async Task<NodeExecutionResult> ExecuteNodeWithParametersAsync(
            string executionId,
            string nodeKey,
            INodeExecutor executor,
            IDictionary<string, object> parameters,
            CancellationToken cancellationToken = default)
        {
            // 已终止的执行不再分发（协调器内部信号量单一闸门，见 ExecuteNodeWithParamsAsync）。
            var state = _stateStore.Get(executionId);
            if (state != null && state.CurrentState == ExecutionStatus.Terminated)
                return new NodeExecutionResult { Success = false };

            var mergedParameters = _parameterStore.MergeParameters(executionId, parameters);

            // 委托协调器统一执行：注册 + 单一并发闸门 + 完成/失败落定（E3：消除 ExecutionStage 自身信号量）
            var result = await _coordinator.ExecuteNodeWithParamsAsync(
                executionId, nodeKey, executor, mergedParameters, cancellationToken).ConfigureAwait(false);

            if (result.Success)
                _parameterStore.StoreNodeResult(executionId, nodeKey, result);

            return result;
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
                var hasRemainingNodes = _coordinator.GetActiveExecutionIds().Contains(executionId);
                if (!hasRemainingNodes)
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

        #region 参数管理

        public void SetExecutionParameter(string executionId, string key, object value)
            => _parameterStore.SetParameter(executionId, key, value);

        public object? GetExecutionParameter(string executionId, string key)
            => _parameterStore.GetParameter(executionId, key);

        public IDictionary<string, object?> GetExecutionParameters(string executionId)
            => _parameterStore.GetParameters(executionId);

        public object? GetNodeResult(string executionId, string nodeKey)
            => _parameterStore.GetNodeResult(executionId, nodeKey);

        #endregion

        #region 状态查询

        public ExecutionStatus GetExecutionStatus(string executionId)
        {
            var state = _stateStore.Get(executionId);
            return state?.CurrentState ?? ExecutionStatus.NotStarted;
        }

        public string? GetCurrentNode(string executionId)
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
            _parameterStore.Cleanup(executionId);
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
                    StateManagement.Diagnostics.StateManagementDiagnosticContext.Record("stage.state.removal_failed", new { executionId, error = ex.Message });
                }
            });
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            // 无托管资源需释放（并发闸门已统一至协调器，E3）。
        }

        #endregion
    }
}
