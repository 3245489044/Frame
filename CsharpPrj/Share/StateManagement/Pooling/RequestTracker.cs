using System.Collections.Concurrent;

namespace Share.StateManagement.Pooling
{
    /// <summary>
    /// RequestTracker —— 请求↔worker 关联追踪（沿用 EcsSessionManager 会话追踪模式）。
    /// 并发闸门/超时/分发属传输层职责，本类仅记录关联。
    /// </summary>
    public sealed class RequestTracker : IRequestTracker
    {
        private readonly ConcurrentDictionary<string, string> _requestToWorker = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, HashSet<string>> _workerRequests = new(StringComparer.Ordinal);
        private int _seq;
        private bool _disposed;

        public int ActiveRequestCount
        {
            get
            {
                ThrowIfDisposed();
                return _requestToWorker.Count;
            }
        }

        public string Assign(string workerId)
        {
            ThrowIfDisposed();
            ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
            var requestId = $"req-{Interlocked.Increment(ref _seq)}";
            _requestToWorker[requestId] = workerId;
            _workerRequests.GetOrAdd(workerId, _ => new HashSet<string>(StringComparer.Ordinal)).Add(requestId);
            return requestId;
        }

        public string? GetWorker(string requestId)
        {
            ThrowIfDisposed();
            return !string.IsNullOrEmpty(requestId) && _requestToWorker.TryGetValue(requestId, out var worker)
                ? worker
                : null;
        }

        public bool Complete(string requestId)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(requestId) || !_requestToWorker.TryRemove(requestId, out var worker))
                return false;

            if (_workerRequests.TryGetValue(worker, out var set))
            {
                set.Remove(requestId);
                // 无残留空集合：worker 全部请求结束后移除其条目，避免集合无界增长。
                if (set.Count == 0)
                    _workerRequests.TryRemove(worker, out _);
            }
            return true;
        }

        public IReadOnlyCollection<string> GetActiveRequestIds()
        {
            ThrowIfDisposed();
            return _requestToWorker.Keys.ToArray();
        }

        public IReadOnlyCollection<string> GetActiveRequestsForWorker(string workerId)
        {
            ThrowIfDisposed();
            return !string.IsNullOrEmpty(workerId) && _workerRequests.TryGetValue(workerId, out var set)
                ? set.ToArray()
                : Array.Empty<string>();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _requestToWorker.Clear();
            _workerRequests.Clear();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RequestTracker));
        }
    }
}
