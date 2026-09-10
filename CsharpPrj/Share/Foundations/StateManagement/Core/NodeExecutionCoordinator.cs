
using System.Collections.Concurrent;

namespace Share.StateManagement
{
    public class NodeExecutionCoordinator : INodeExecutionCoordinator
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, TaskCompletionSource<bool>>> _nodeCompletions = new();
        private readonly ConcurrentDictionary<string, HashSet<string>> _activeNodeExecutions = new();
        private readonly ConcurrentDictionary<string, string> _currentNodes = new();
        private readonly SemaphoreSlim _executionSemaphore;
        private readonly IExecutionStateStore<ExecutionStatus>? _stateStore;

        public int MaxConcurrency { get; set; }

        public NodeExecutionCoordinator(int maxConcurrency = 10, IExecutionStateStore<ExecutionStatus>? stateStore = null)
        {
            MaxConcurrency = maxConcurrency;
            _executionSemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            _stateStore = stateStore;
        }

        public bool TryRegisterNode(string executionId, string nodeKey, out bool isDuplicate)
        {
            isDuplicate = false;

            if (string.IsNullOrWhiteSpace(executionId))
                throw new ArgumentException("Execution ID cannot be empty", nameof(executionId));
            if (string.IsNullOrWhiteSpace(nodeKey))
                throw new ArgumentException("Node key cannot be empty", nameof(nodeKey));

            if (_stateStore != null)
            {
                var state = _stateStore.Get(executionId);
                if (state != null && state.IsFinished)
                    return false;
            }

            var activeNodes = _activeNodeExecutions.GetOrAdd(executionId, _ => new HashSet<string>());
            lock (activeNodes)
            {
                if (activeNodes.Contains(nodeKey))
                {
                    isDuplicate = true;
                    return false;
                }
                activeNodes.Add(nodeKey);
            }

            var nodeCompletions = _nodeCompletions.GetOrAdd(executionId, _ => new ConcurrentDictionary<string, TaskCompletionSource<bool>>());
            nodeCompletions[nodeKey] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return true;
        }

        private bool IsExecutionTerminated(string executionId)
        {
            if (_stateStore == null) return false;
            var state = _stateStore.Get(executionId);
            return state != null && state.CurrentState == ExecutionStatus.Terminated;
        }

        private async Task<TResult> ExecuteWithSemaphoreAsync<TResult>(
            string executionId,
            string nodeKey,
            Func<Task<TResult>> executeFunc,
            Func<Exception, TResult> errorResultFactory,
            CancellationToken cancellationToken)
        {
            try
            {
                await _executionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (IsExecutionTerminated(executionId))
                        return errorResultFactory(new OperationCanceledException("执行已被终止"));

                    _currentNodes[executionId] = nodeKey;
                    return await executeFunc().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    MarkNodeFailed(executionId, nodeKey, ex);
                    return errorResultFactory(ex);
                }
                finally
                {
                    _executionSemaphore.Release();
                }
            }
            catch (OperationCanceledException ex)
            {
                MarkNodeFailed(executionId, nodeKey, ex);
                return errorResultFactory(ex);
            }
        }

        public async Task<bool> ExecuteNodeAsync(
            string executionId,
            string nodeKey,
            Func<Task> executeFunc,
            CancellationToken cancellationToken = default)
        {
            if (!TryRegisterNode(executionId, nodeKey, out _))
                return false;

            return await ExecuteWithSemaphoreAsync(
                executionId,
                nodeKey,
                async () =>
                {
                    await executeFunc().ConfigureAwait(false);
                    MarkNodeCompleted(executionId, nodeKey);
                    return true;
                },
                _ => false,
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<NodeExecutionResult> ExecuteNodeWithParamsAsync(
            string executionId,
            string nodeKey,
            INodeExecutor executor,
            IDictionary<string, object> parameters,
            CancellationToken cancellationToken = default)
        {
            if (!TryRegisterNode(executionId, nodeKey, out _))
                return new NodeExecutionResult { Success = false };

            return await ExecuteWithSemaphoreAsync(
                executionId,
                nodeKey,
                async () =>
                {
                    var result = await executor.ExecuteAsync(nodeKey, parameters, cancellationToken).ConfigureAwait(false);

                    if (result.Success)
                        MarkNodeCompleted(executionId, nodeKey);
                    else
                        MarkNodeFailed(executionId, nodeKey, result.Error ?? new Exception("节点执行失败"));

                    return result;
                },
                ex => new NodeExecutionResult { Success = false, Error = ex },
                cancellationToken).ConfigureAwait(false);
        }

        public async Task WaitForNodeCompletionAsync(
            string executionId,
            string nodeKey,
            CancellationToken cancellationToken = default)
        {
            if (IsNodeCompleted(executionId, nodeKey))
                return;

            if (_nodeCompletions.TryGetValue(executionId, out var nodeCompletions) &&
                nodeCompletions.TryGetValue(nodeKey, out var tcs))
            {
                using var ctr = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
                await tcs.Task.ConfigureAwait(false);
            }
            else
            {
                if (!IsNodeCompleted(executionId, nodeKey))
                    throw new InvalidOperationException($"Node {nodeKey} has not been registered for execution {executionId}.");
            }
        }

        public void MarkNodeCompleted(string executionId, string nodeKey)
        {
            RemoveNodeFromActiveList(executionId, nodeKey);
            CompleteNodeTask(executionId, nodeKey, true, null);
        }

        public void MarkNodeFailed(string executionId, string nodeKey, Exception error)
        {
            RemoveNodeFromActiveList(executionId, nodeKey);
            CompleteNodeTask(executionId, nodeKey, false, error);
        }

        private void RemoveNodeFromActiveList(string executionId, string nodeKey)
        {
            if (_activeNodeExecutions.TryGetValue(executionId, out var activeNodes))
            {
                lock (activeNodes)
                {
                    activeNodes.Remove(nodeKey);
                }
            }

            _currentNodes.TryRemove(executionId, out _);
        }

        public bool IsNodeCompleted(string executionId, string nodeKey)
        {
            if (_nodeCompletions.TryGetValue(executionId, out var nodeCompletions))
            {
                if (nodeCompletions.TryGetValue(nodeKey, out var tcs))
                    return tcs.Task.IsCompleted;
            }
            return false;
        }

        public string? GetCurrentNode(string executionId)
        {
            return _currentNodes.TryGetValue(executionId, out var nodeKey) ? nodeKey : null;
        }

        public void SetCurrentNode(string executionId, string nodeKey)
        {
            _currentNodes[executionId] = nodeKey;
        }

        public IEnumerable<string> GetActiveExecutionIds()
        {
            return _activeNodeExecutions
                .Where(kv => kv.Value.Count > 0)
                .Select(kv => kv.Key);
        }

        public void CancelAll(string executionId)
        {
            if (_nodeCompletions.TryRemove(executionId, out var nodeCompletions))
            {
                foreach (var tcs in nodeCompletions.Values)
                    tcs.TrySetCanceled();
            }

            _activeNodeExecutions.TryRemove(executionId, out _);
            _currentNodes.TryRemove(executionId, out _);
        }

        private void CompleteNodeTask(string executionId, string nodeKey, bool success, Exception? error)
        {
            if (_nodeCompletions.TryGetValue(executionId, out var nodeCompletions))
            {
                if (nodeCompletions.TryRemove(nodeKey, out var tcs))
                {
                    if (success)
                        tcs.TrySetResult(true);
                    else
                        tcs.TrySetException(error ?? new Exception("节点执行失败"));
                }
            }
        }
    }
}
