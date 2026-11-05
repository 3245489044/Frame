using System.Collections.Concurrent;
using ExecutionCenter.Abstractions;

namespace ExecutionCenter.Core;

internal static class PriorityMapper
{
    public static int MapToQueuePriority(int executionCenterPriority)
        => int.MaxValue - executionCenterPriority;
}

public class TaskQueue : IDisposable
{
    private const int PollIntervalMs = 500;

    private readonly InternalPriorityQueue<ITaskMessage> _queue = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<ITaskResponse>> _pending = new();
    private readonly object _disposeLock = new();
    private bool _disposed;

    public int PendingCount => _pending.Count;

    public int QueueCount => _queue.Count;

    public async Task<ITaskResponse> EnqueueAsync(ITaskMessage task, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<ITaskResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[task.MessageId] = tcs;

        using var reg = ct.Register(() => TryCancel(task.MessageId));

        var queuePriority = PriorityMapper.MapToQueuePriority(task.Priority);
        _queue.StoreMessage(task, queuePriority);
        return await tcs.Task;
    }

    public ITaskMessage Dequeue(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var message = _queue.RetrieveMessage(PollIntervalMs);
            if (message != null)
                return message;
        }

        ct.ThrowIfCancellationRequested();
        throw new InvalidOperationException("Unreachable code in Dequeue");
    }

    public void Complete(ITaskResponse response)
    {
        if (_pending.TryRemove(response.RequestMessageId, out var tcs))
            tcs.TrySetResult(response);
    }

    private void TryCancel(Guid id)
    {
        if (_pending.TryRemove(id, out var tcs))
            tcs.TrySetCanceled();
    }

    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _queue.Dispose();
    }
}
