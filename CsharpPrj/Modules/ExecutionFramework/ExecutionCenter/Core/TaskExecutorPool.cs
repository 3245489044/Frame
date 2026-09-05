using System.Collections.Concurrent;
using ExecutionCenter.Abstractions;
using ExecutionCenter.Messages;

namespace ExecutionCenter.Core;

public class TaskExecutorPool : ITaskExecutorPool
{
    private readonly TaskQueue _queue = new();
    private readonly object? _worker;
    private readonly ConcurrentDictionary<string, Func<ITaskMessage, object?, CancellationToken, Task<ITaskResponse>>> _handlers = new();
    private readonly object _startStopLock = new();

    private Task? _loopTask;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public object? Worker => _worker;

    public TaskQueue Queue => _queue;

    public bool IsRunning
    {
        get
        {
            lock (_startStopLock)
            {
                return _loopTask != null && !_loopTask.IsCompleted;
            }
        }
    }

    public TaskExecutorPool(object? worker = null)
    {
        _worker = worker;
    }

    public void RegisterHandler(string taskType,
        Func<ITaskMessage, object?, CancellationToken, Task<ITaskResponse>> handler)
        => _handlers[taskType] = handler;

    public Task<ITaskResponse> SubmitAsync(ITaskMessage task, CancellationToken ct = default)
        => _queue.EnqueueAsync(task, ct);

    public void Start()
    {
        lock (_startStopLock)
        {
            if (_loopTask != null && !_loopTask.IsCompleted)
                return;

            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => LoopAsync(_cts.Token));
        }
    }

    public void Stop()
    {
        Task? taskToWait;
        CancellationTokenSource? ctsToCancel;

        lock (_startStopLock)
        {
            ctsToCancel = _cts;
            _cts = null;
            taskToWait = _loopTask;
        }

        ctsToCancel?.Cancel();

        if (taskToWait != null)
        {
            try
            {
                taskToWait.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
            catch (AggregateException ae)
            {
                ae.Handle(ex => ex is OperationCanceledException);
            }
        }

        lock (_startStopLock)
        {
            if (_loopTask == taskToWait)
                _loopTask = null;
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var task = _queue.Dequeue(ct);

                if (_handlers.TryGetValue(task.TaskType, out var handler))
                {
                    var response = await handler(task, _worker, ct);
                    _queue.Complete(response);
                }
                else
                {
                    _queue.Complete(new TaskResponse(
                        task.MessageId, false, error: $"Unknown task type: {task.TaskType}"));
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception)
            {
            }
        }
    }

    public void Dispose()
    {
        lock (_startStopLock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        Stop();
        _queue.Dispose();
        _cts?.Dispose();
    }
}
