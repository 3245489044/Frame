using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.StateMachine
{
    #region 状态管理器

    public class StateManager<TState> : IDisposable
    {
        private readonly ConcurrentDictionary<string, StateRecord<TState>> _records = new(StringComparer.Ordinal);
        private readonly SemaphoreSlim _globalSemaphore;
        private bool _disposed;

        public int Count => _records.Count;

        public int MaxConcurrency => _globalSemaphore.CurrentCount;

        public StateManager(int maxConcurrency = 10)
        {
            if (maxConcurrency < 1)
                throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "最大并发数必须大于 0");

            _globalSemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        }

        public StateRecord<TState> GetOrCreateRecord(string instanceId, TState initialState = default!)
        {
            return _records.GetOrAdd(instanceId, id => new StateRecord<TState>(id, initialState));
        }

        public bool TryGetRecord(string instanceId, out StateRecord<TState>? record)
        {
            return _records.TryGetValue(instanceId, out record);
        }

        public bool TryRegisterAction(string instanceId, string actionName)
        {
            var record = GetOrCreateRecord(instanceId);

            lock (record)
            {
                if (record.IsActionCompleted(actionName) || record.IsActionFailed(actionName))
                    return false;

                record.SetCurrentAction(actionName);
            }

            return true;
        }

        public async Task<bool> ExecuteActionAsync(
            string instanceId,
            string actionName,
            Func<Task> executeFunc,
            CancellationToken cancellationToken = default)
        {
            if (!TryRegisterAction(instanceId, actionName))
                return false;

            try
            {
                await _globalSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    await executeFunc().ConfigureAwait(false);
                    var record = GetOrCreateRecord(instanceId);
                    record.AddCompletedAction(actionName);
                    return true;
                }
                catch (Exception ex)
                {
                    var record = GetOrCreateRecord(instanceId);
                    record.AddFailedAction(actionName, ex.Message);
                    return false;
                }
                finally
                {
                    _globalSemaphore.Release();
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public void MarkActionCompleted(string instanceId, string actionName)
        {
            if (_records.TryGetValue(instanceId, out var record))
            {
                lock (record) { record.AddCompletedAction(actionName); }
            }
        }

        public void MarkActionFailed(string instanceId, string actionName, string error)
        {
            if (_records.TryGetValue(instanceId, out var record))
            {
                lock (record) { record.AddFailedAction(actionName, error); }
            }
        }

        public bool RemoveRecord(string instanceId)
        {
            return _records.TryRemove(instanceId, out _);
        }

        public IEnumerable<string> GetAllInstanceIds() => _records.Keys;

        public void Clear()
        {
            _records.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _globalSemaphore.Dispose();
            _records.Clear();
            GC.SuppressFinalize(this);
        }
    }

    #endregion
}
