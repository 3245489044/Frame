
using System.Collections.Concurrent;
using System.Reflection;

namespace Modules.EventFramework.Utils;

public static class ReflectionCache
{
    private static readonly ConcurrentDictionary<(Type, string), EventInfo?> _eventInfoCache = new();

    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> _propertyInfoCache = new();

    public static EventInfo? GetOrAddEventInfo(Type controlType, string eventName)
    {
        return _eventInfoCache.GetOrAdd(
            (controlType, eventName),
            key => key.Item1.GetEvent(key.Item2,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));
    }

    public static PropertyInfo? GetOrAddPropertyInfo(Type type, string propertyName)
    {
        return _propertyInfoCache.GetOrAdd(
            (type, propertyName),
            key => key.Item1.GetProperty(key.Item2,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));
    }

    public static void Clear()
    {
        _eventInfoCache.Clear();
        _propertyInfoCache.Clear();
    }

    public static int EventInfoCacheCount => _eventInfoCache.Count;

    public static int PropertyInfoCacheCount => _propertyInfoCache.Count;
}
