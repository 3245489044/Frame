
using System.Collections.Concurrent;
using Modules.EventFramework.Abstractions;
using Modules.EventFramework.Bus;
using Timer = System.Threading.Timer;

namespace Modules.EventFramework.Bus.Decorators;

public class DeduplicatingEventBusDecorator : EventBusCoreDecoratorBase
{
    private readonly TimeSpan _dedupWindow;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DateTime>> _processedEvents = new();
    private readonly Timer? _cleanupTimer;
    private bool _disposed;

    public DeduplicatingEventBusDecorator(IEventBusCore inner, TimeSpan? dedupWindow = null, TimeSpan? cleanupInterval = null)
        : base(inner)
    {
        _dedupWindow = dedupWindow ?? TimeSpan.FromSeconds(5);
        var interval = cleanupInterval ?? TimeSpan.FromSeconds(60);
        if (interval > TimeSpan.Zero)
        {
            _cleanupTimer = new Timer(_ => CleanupExpiredEntries(), null, interval, interval);
        }
    }

    private static string GenerateFingerprint(string eventName, object? data)
    {
        string dataRef;
        if (data == null)
        {
            dataRef = "null";
        }
        else if (data is string str)
        {
            dataRef = str;
        }
        else
        {
            try
            {
                dataRef = System.Text.Json.JsonSerializer.Serialize(data);
            }
            catch
            {
                dataRef = data.ToString() ?? data.GetType().Name;
            }
        }

        return $"{eventName}:{dataRef}";
    }

    public override void Publish(string eventName, object? data = null)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("事件名称不能为空", nameof(eventName));

        if (TryDeduplicate(eventName, data))
            return;

        Inner.Publish(eventName, data);
    }

    public override void Publish<TEvent>(TEvent @event)
    {
        if (@event == null) throw new ArgumentNullException(nameof(@event));

        var eventName = typeof(TEvent).Name;
        if (TryDeduplicate(eventName, @event))
            return;

        Inner.Publish(@event);
    }

    private bool TryDeduplicate(string eventName, object? data)
    {
        var fingerprint = GenerateFingerprint(eventName, data);

        if (_processedEvents.TryGetValue(eventName, out var fingerprints) &&
            fingerprints.TryGetValue(fingerprint, out var processedAt) &&
            DateTime.UtcNow - processedAt < _dedupWindow)
        {
            return true;
        }

        var fingerprintsDict = _processedEvents.GetOrAdd(eventName, _ => new ConcurrentDictionary<string, DateTime>());
        fingerprintsDict[fingerprint] = DateTime.UtcNow;

        return false;
    }

    public override void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _cleanupTimer?.Dispose();
            _processedEvents.Clear();
            base.Dispose();
        }
    }

    private void CleanupExpiredEntries()
    {
        var cutoff = DateTime.UtcNow - _dedupWindow;
        foreach (var kvp in _processedEvents)
        {
            var expiredFingerprints = kvp.Value
                .Where(fp => fp.Value < cutoff)
                .Select(fp => fp.Key)
                .ToList();

            foreach (var fingerprint in expiredFingerprints)
                kvp.Value.TryRemove(fingerprint, out _);

            if (kvp.Value.IsEmpty)
                _processedEvents.TryRemove(kvp.Key, out _);
        }
    }
}
