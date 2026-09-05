
using System.Collections.Concurrent;
using Modules.EventFramework.Abstractions;
using TConfig;
using IEventMappingRegistry = TConfig.IEventMappingRegistry;

namespace Modules.EventFramework.Core;

public class DefaultEventMappingRegistry : IEventMappingRegistry
{
    private readonly IEventNameNormalizer _normalizer;
    private readonly ConcurrentDictionary<string, List<EventMappingEntry>> _mappings = new(StringComparer.OrdinalIgnoreCase);

    public DefaultEventMappingRegistry(IEventNameNormalizer? normalizer = null)
    {
        _normalizer = normalizer ?? new DefaultEventNameNormalizer();
    }

    public void Register(string eventName, EventAccessor accessor, int priority = 100)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("事件名不能为空", nameof(eventName));
        ArgumentNullException.ThrowIfNull(accessor);

        var normalizedName = _normalizer.ToPascalCase(eventName);
        var entry = new EventMappingEntry(normalizedName, accessor, priority);
        _mappings.AddOrUpdate(normalizedName,
            _ => new List<EventMappingEntry> { entry },
            (_, list) =>
            {
                var insertIndex = list.FindIndex(e => e.Priority > priority);
                if (insertIndex < 0)
                    list.Add(entry);
                else
                    list.Insert(insertIndex, entry);
                return list;
            });
    }

    public void RegisterRange(IEnumerable<EventMappingEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        foreach (var entry in entries)
        {
            Register(entry.EventName, entry.Accessor, entry.Priority);
        }
    }

    public EventAccessor? Find(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            return null;

        var normalizedName = _normalizer.ToPascalCase(eventName);
        if (_mappings.TryGetValue(normalizedName, out var entries) && entries is { Count: > 0 })
            return entries[0].Accessor;
        return null;
    }

    public IEnumerable<EventAccessor> FindAll(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            return Enumerable.Empty<EventAccessor>();

        var normalizedName = _normalizer.ToPascalCase(eventName);
        if (_mappings.TryGetValue(normalizedName, out var entries) && entries is { Count: > 0 })
            return entries.Select(e => e.Accessor);
        return Enumerable.Empty<EventAccessor>();
    }

    public void Unregister(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            return;

        var normalizedName = _normalizer.ToPascalCase(eventName);
        _mappings.TryRemove(normalizedName, out _);
    }

    public void Clear()
    {
        _mappings.Clear();
    }

    public IEnumerable<string> GetAllEventNames()
    {
        return _mappings.Keys.ToList();
    }
}
