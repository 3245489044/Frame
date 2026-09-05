
using System.Collections.Concurrent;
using Modules.EventFramework.Abstractions;

namespace Modules.EventFramework.Bus;

public class EventQueue : IDisposable
{
    private readonly ConcurrentQueue<PropagationEvent> _queue = new();
    private readonly CancellationTokenSource _cts = new();
    private int _isProcessing;
    private readonly IEventBusCore _bus;

    public const int MaxLength = 1000;

    public int Count => _queue.Count;

    public Action<PropagationEvent>? OnOverflowDiscard { get; set; }

    public EventQueue(IEventBusCore bus)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    }

    public void Enqueue(PropagationEvent propagationEvent)
    {
        if (propagationEvent == null) throw new ArgumentNullException(nameof(propagationEvent));
        if (propagationEvent.IsCancelled) return;

        if (_queue.Count >= MaxLength)
        {
            OnOverflowDiscard?.Invoke(propagationEvent);
            return;
        }

        _queue.Enqueue(propagationEvent);
        TryProcessAsync();
    }

    private void TryProcessAsync()
    {
        if (Interlocked.CompareExchange(ref _isProcessing, 1, 0) == 0)
            Task.Run(() => ProcessAsync());
    }

    private async Task ProcessAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested && _queue.TryDequeue(out var evt))
            {
                if (!evt.IsCancelled)
                {
                    _bus.Publish(evt.EventName, evt);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
        finally
        {
            Interlocked.Exchange(ref _isProcessing, 0);

            if (!_queue.IsEmpty)
                TryProcessAsync();
        }
    }

    public void Clear()
    {
        while (_queue.TryDequeue(out _)) { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        Clear();
        _cts.Dispose();
    }
}
