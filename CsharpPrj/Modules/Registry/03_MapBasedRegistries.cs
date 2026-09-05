
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Modules.Registry;


public class SetRegistryBase<TKey> : RegistryBase<TKey>, ISetRegistry<TKey>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, byte> _store;

    public SetRegistryBase(IEqualityComparer<TKey>? comparer = null)
    {
        _store = new ConcurrentDictionary<TKey, byte>(
            comparer ?? EqualityComparer<TKey>.Default);
    }

    protected ConcurrentDictionary<TKey, byte> Store => _store;


    protected override bool StoreContainsKey(TKey key) => _store.ContainsKey(key);

    protected override bool StoreTryRemove(TKey key) => _store.TryRemove(key, out _);

    protected override void StoreClear() => _store.Clear();


    protected virtual void OnBeforeRegister(TKey key) { }

    public virtual void Register(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        OnBeforeRegister(key);
        _store[key] = 0;
    }

    public IEnumerable<TKey> GetKeys() => _store.Keys;

    public int Count => _store.Count;
}


public class MapRegistryBase<TKey, TValue> : RegistryBase<TKey>, IMapRegistry<TKey, TValue>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, TValue> _store;
    private readonly bool _allowOverwrite;

    public MapRegistryBase(IEqualityComparer<TKey>? comparer = null, bool allowOverwrite = true)
    {
        _store = new ConcurrentDictionary<TKey, TValue>(comparer ?? EqualityComparer<TKey>.Default);
        _allowOverwrite = allowOverwrite;
    }

    protected ConcurrentDictionary<TKey, TValue> Store => _store;


    protected override bool StoreContainsKey(TKey key) => _store.ContainsKey(key);

    protected override bool StoreTryRemove(TKey key) => _store.TryRemove(key, out _);

    protected override void StoreClear() => _store.Clear();


    protected virtual void OnBeforeRegister(TKey key, TValue value) { }

    protected virtual void OnAfterRegister(TKey key, TValue value, bool isUpdate) { }

    public virtual void Register(TKey key, TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        OnBeforeRegister(key, value);

        var isUpdate = _store.ContainsKey(key);
        if (!_allowOverwrite && isUpdate)
            throw new InvalidOperationException($"Key '{key}' is already registered.");

        _store[key] = value;
        OnAfterRegister(key, value, isUpdate);
    }

    public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _store.TryGetValue(key, out value);
    }

    public IEnumerable<TKey> GetKeys() => _store.Keys;


    public virtual void RegisterRange(IEnumerable<KeyValuePair<TKey, TValue>> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var kvp in items)
            Register(kvp.Key, kvp.Value);
    }

    public virtual void UnregisterRange(IEnumerable<TKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        foreach (var key in keys)
            Unregister(key);
    }


    public int Count => _store.Count;


    public IReadOnlyCollection<TValue> GetValues()
        => _store.Values.ToList().AsReadOnly();

    public IReadOnlyCollection<TValue> Filter(Func<TValue, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return _store.Values.Where(predicate).ToList().AsReadOnly();
    }

    public IReadOnlyCollection<KeyValuePair<TKey, TValue>> FilterEntries(
        Func<TKey, TValue, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return _store.Where(kvp => predicate(kvp.Key, kvp.Value))
                     .ToList().AsReadOnly();
    }
}


public class FactoryRegistryBase<TKey, TValue> : RegistryBase<TKey>, IFactoryRegistry<TKey, TValue>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Func<IServiceProvider?, TValue>> _factories;

    public FactoryRegistryBase(IEqualityComparer<TKey>? comparer = null)
    {
        _factories = new ConcurrentDictionary<TKey, Func<IServiceProvider?, TValue>>(
            comparer ?? EqualityComparer<TKey>.Default);
    }

    protected ConcurrentDictionary<TKey, Func<IServiceProvider?, TValue>> Factories => _factories;


    protected override bool StoreContainsKey(TKey key) => _factories.ContainsKey(key);

    protected override bool StoreTryRemove(TKey key) => _factories.TryRemove(key, out _);

    protected override void StoreClear() => _factories.Clear();


    protected virtual void OnBeforeRegister(TKey key) { }

    public virtual void Register(TKey name, Func<IServiceProvider?, TValue> factory)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(factory);
        OnBeforeRegister(name);
        _factories[name] = factory;
    }

    public TValue? Resolve(TKey name, IServiceProvider? serviceProvider = null)
    {
        if (_factories.TryGetValue(name, out var factory))
            return factory(serviceProvider);
        return default;
    }

    public bool TryResolve(TKey name, out TValue? value, IServiceProvider? serviceProvider = null)
    {
        if (_factories.TryGetValue(name, out var factory))
        {
            try { value = factory(serviceProvider); return true; }
            catch { value = default; return false; }
        }
        value = default;
        return false;
    }

    public IEnumerable<TKey> GetKeys() => _factories.Keys;

    public int Count => _factories.Count;
}

