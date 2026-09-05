
namespace Modules.Registry;


public static class SetRegistryExtensions
{
    public static void RegisterRange<TKey>(this ISetRegistry<TKey> registry, IEnumerable<TKey> keys)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(keys);
        foreach (var key in keys)
            registry.Register(key);
    }

    public static void UnregisterRange<TKey>(this ISetRegistry<TKey> registry, IEnumerable<TKey> keys)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(keys);
        foreach (var key in keys)
            registry.Unregister(key);
    }

    public static TKey[] GetKeysArray<TKey>(this ISetRegistry<TKey> registry)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(registry);
        return registry.GetKeys().ToArray();
    }
}


public class ObservableMapRegistry<TKey, TValue> : MapRegistryBase<TKey, TValue>,
    INotifyRegistry<TKey, TValue>
    where TKey : notnull
{
    public event EventHandler<RegistryChangedEventArgs<TKey, TValue>>? ItemRegistered;

    public event EventHandler<RegistryChangedEventArgs<TKey, TValue>>? ItemUpdated;

    public event EventHandler<RegistryChangedEventArgs<TKey, TValue>>? ItemUnregistered;

    public event EventHandler? RegistryCleared;

    public ObservableMapRegistry(
        IEqualityComparer<TKey>? comparer = null,
        bool allowOverwrite = true)
        : base(comparer, allowOverwrite) { }

    private TValue? _pendingOldValue;
    private bool _hasPendingOldValue;
    private readonly object _eventLock = new();

    protected override void OnBeforeRegister(TKey key, TValue value)
    {
        base.OnBeforeRegister(key, value);
        lock (_eventLock)
        {
            Store.TryGetValue(key, out var old);
            _pendingOldValue = old;
            _hasPendingOldValue = true;
        }
    }

    protected override void OnAfterRegister(TKey key, TValue value, bool isUpdate)
    {
        base.OnAfterRegister(key, value, isUpdate);

        TValue? oldValue;
        bool hasOldValue;
        lock (_eventLock)
        {
            oldValue = _pendingOldValue;
            hasOldValue = _hasPendingOldValue;
            _pendingOldValue = default;
            _hasPendingOldValue = false;
        }

        if (isUpdate)
        {
            ItemUpdated?.Invoke(this, new RegistryChangedEventArgs<TKey, TValue>
            {
                ChangeType = RegistryChangeType.Updated,
                Key = key,
                OldValue = hasOldValue ? oldValue : default,
                NewValue = value
            });
        }
        else
        {
            ItemRegistered?.Invoke(this, new RegistryChangedEventArgs<TKey, TValue>
            {
                ChangeType = RegistryChangeType.Registered,
                Key = key,
                NewValue = value
            });
        }
    }

    protected override void OnBeforeUnregister(TKey key)
    {
        base.OnBeforeUnregister(key);
        var oldValue = Store.TryGetValue(key, out var old) ? old : default;
        ItemUnregistered?.Invoke(this, new RegistryChangedEventArgs<TKey, TValue>
        {
            ChangeType = RegistryChangeType.Unregistered,
            Key = key,
            OldValue = oldValue
        });
    }

    protected override void OnBeforeClear()
    {
        base.OnBeforeClear();
        RegistryCleared?.Invoke(this, EventArgs.Empty);
    }
}
