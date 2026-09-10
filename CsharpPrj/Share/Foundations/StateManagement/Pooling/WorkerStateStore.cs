using System.Collections.Concurrent;
using TConfig;
using TConfig.ContextFrame.Abstractions;

namespace Share.StateManagement.Pooling
{
    /// <summary>
    /// WorkerStateStore —— 每 worker 一个 StorageEngine 会话（StorageCell）的隔离状态存储 + 状态机。
    /// 元数据写穿到会话 cell（Config 层：pid/startedAt；Session 层：state/lastActivity/lastError/timeoutCount），
    /// 内存快照字典供 O(1) 读取与 StateChanged 事件源。
    /// 进程/IPC/超时不在本类（保持传输无关）。
    /// </summary>
    public sealed class WorkerStateStore : IWorkerStateStore
    {
        private const string StateKey = "state";
        private const string PidKey = "pid";
        private const string StartedAtKey = "started_at_utc";
        private const string LastActivityKey = "last_activity_utc";
        private const string LastErrorKey = "last_error";
        private const string TimeoutCountKey = "timeout_count";

        private readonly IStorageEngine _engine;
        private readonly ConcurrentDictionary<string, WorkerEntry> _workers = new(StringComparer.Ordinal);
        private bool _disposed;

        /// <summary>单 worker 条目：内存快照 + 已缓存会话句柄（避免每次写入重复查引擎）。</summary>
        private sealed record WorkerEntry(WorkerStateInfo Info, IStorageCell Cell);

        public event Action<WorkerStateInfo>? StateChanged;

        public WorkerStateStore(IStorageEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public IStorageCell GetOrCreate(string workerId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
            ThrowIfDisposed();

            // 已登记：直接返回缓存会话句柄，不重建引擎会话、不重写快照（保持两源一致）。
            if (_workers.TryGetValue(workerId, out var existing))
                return existing.Cell;

            var cell = _engine.GetSession(workerId);
            var created = cell == null;
            cell ??= _engine.CreateSession(workerId);

            var initial = WorkerStateInfo.Initial(workerId);
            var entry = new WorkerEntry(initial, cell);

            // 原子登记：并发下只有一方胜出；败者回收本次新建的孤儿会话，避免会话泄漏与两源不一致（E2）。
            var winner = _workers.GetOrAdd(workerId, entry);
            if (ReferenceEquals(winner, entry))
            {
                WriteCell(cell, initial);
                StateChanged?.Invoke(initial);
            }
            else if (created)
            {
                _engine.DisposeSession(workerId);
            }

            return winner.Cell;
        }

        public IStorageCell? GetCell(string workerId)
            => _workers.TryGetValue(workerId, out var entry) ? entry.Cell : null;

        public bool Exists(string workerId) => _workers.ContainsKey(workerId);

        public WorkerStateInfo Get(string workerId)
            => _workers.TryGetValue(workerId, out var entry)
                ? entry.Info
                : throw new KeyNotFoundException($"Worker '{workerId}' 未登记（请先调用 GetOrCreate）。");

        public bool TryGet(string workerId, out WorkerStateInfo? info)
        {
            if (_workers.TryGetValue(workerId, out var entry))
            {
                info = entry.Info;
                return true;
            }
            info = null;
            return false;
        }

        public bool Transition(string workerId, PoolWorkerState from, PoolWorkerState to)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
            ThrowIfDisposed();

            if (!_workers.TryGetValue(workerId, out var entry))
                throw new KeyNotFoundException($"Worker '{workerId}' 未登记。");

            if (entry.Info.State != from)
                return false; // from 守卫：非法转移拒绝

            var updated = entry.Info with
            {
                State = to,
                LastActivityUtc = DateTime.UtcNow,
            };

            // CAS：仅当快照自读取后未变才交换，杜绝 check-then-act 竞态下非法转移被放行。
            if (!_workers.TryUpdate(workerId, entry with { Info = updated }, entry))
                return false; // 竞态：其他线程已转移，本次拒绝

            WriteCell(entry.Cell, updated);
            StateChanged?.Invoke(updated);
            return true;
        }


        public void SetPid(string workerId, int pid)
        {
            Update(workerId, (current, cell) =>
            {
                var updated = current with { Pid = pid, LastActivityUtc = DateTime.UtcNow };
                cell.Set(PidKey, pid, StorageLayer.Config);
                return updated;
            });
        }

        public void UpdateLastActivity(string workerId)
        {
            Update(workerId, (current, cell) =>
            {
                var updated = current with { LastActivityUtc = DateTime.UtcNow };
                cell.Set(LastActivityKey, updated.LastActivityUtc, StorageLayer.Session);
                return updated;
            });
        }

        public void RecordError(string workerId, string? error)
        {
            Update(workerId, (current, cell) =>
            {
                var updated = current with { LastError = error, LastActivityUtc = DateTime.UtcNow };
                cell.Set(LastErrorKey, error, StorageLayer.Session);
                return updated;
            });
        }

        public void IncrementTimeoutCount(string workerId)
        {
            Update(workerId, (current, cell) =>
            {
                var updated = current with { TimeoutCount = current.TimeoutCount + 1, LastActivityUtc = DateTime.UtcNow };
                cell.Set(TimeoutCountKey, updated.TimeoutCount, StorageLayer.Session);
                return updated;
            });
        }

        public bool Remove(string workerId)
        {
            var removed = _workers.TryRemove(workerId, out _);
            _engine.DisposeSession(workerId);
            return removed;
        }

        public IReadOnlyCollection<string> GetWorkerIds() => _workers.Keys.ToArray();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var id in _workers.Keys.ToList())
            {
                _engine.DisposeSession(id);
            }
            _workers.Clear();
        }

        private void Update(string workerId, Func<WorkerStateInfo, IStorageCell, WorkerStateInfo> apply)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
            ThrowIfDisposed();

            if (!_workers.TryGetValue(workerId, out var entry))
                throw new KeyNotFoundException($"Worker '{workerId}' 未登记。");

            var updated = apply(entry.Info, entry.Cell);

            // CAS 防并发覆写：快照自读取后已被他线程更新则拒绝本次写入。
            if (!_workers.TryUpdate(workerId, entry with { Info = updated }, entry))
                throw new InvalidOperationException($"Worker '{workerId}' 状态并发变更，更新被拒绝。");

            StateChanged?.Invoke(updated);
        }

        private static void WriteCell(IStorageCell cell, WorkerStateInfo info)
        {
            cell.Set(PidKey, info.Pid, StorageLayer.Config);
            cell.Set(StartedAtKey, info.StartedAtUtc, StorageLayer.Config);
            cell.Set(StateKey, info.State, StorageLayer.Session);
            cell.Set(LastActivityKey, info.LastActivityUtc, StorageLayer.Session);
            cell.Set(LastErrorKey, info.LastError, StorageLayer.Session);
            cell.Set(TimeoutCountKey, info.TimeoutCount, StorageLayer.Session);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(WorkerStateStore));
        }
    }
}

