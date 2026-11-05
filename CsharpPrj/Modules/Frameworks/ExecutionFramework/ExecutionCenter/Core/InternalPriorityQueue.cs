
using System.Collections.Concurrent;

namespace ExecutionCenter.Core;

internal class InternalPriorityQueue<T>
{
    private readonly ConcurrentDictionary<int, ConcurrentQueue<T>> _queues = new();
    private readonly AutoResetEvent _signal = new AutoResetEvent(false);
    private volatile bool _disposed;

    public int Count => _queues.Sum(q => q.Value.Count);
    public bool IsEmpty => Count == 0;

    public void StoreMessage(T message, int priority)
    {
        var queue = _queues.GetOrAdd(priority, _ => new ConcurrentQueue<T>());
        queue.Enqueue(message);
        _signal.Set();
    }

    public T? TryRetrieveMessage()
    {
        var priorities = _queues.Keys.OrderByDescending(p => p).ToList();
        foreach (var priority in priorities)
        {
            if (_queues.TryGetValue(priority, out var queue) && queue.TryDequeue(out var message))
            {
                if (queue.IsEmpty) _queues.TryRemove(priority, out _);
                return message;
            }
        }
        return default;
    }

    public T? RetrieveMessage(int timeoutMs)
    {
        var startTime = DateTime.UtcNow;

        while (true)
        {
            var message = TryRetrieveMessage();
            if (message != null) return message;

            if (timeoutMs >= 0)
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                if (elapsed > timeoutMs) return default;

                var remaining = (int)(timeoutMs - elapsed);
                if (remaining <= 0) return default;
                _signal.WaitOne(remaining);
            }
            else
            {
                _signal.WaitOne();
            }

            if (_disposed) return default;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _signal.Set();
        _signal.Dispose();
    }
}