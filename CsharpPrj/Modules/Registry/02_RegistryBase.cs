
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Modules.Registry;

public abstract class RegistryBase<TKey> : IRegistry
    where TKey : notnull
{

    protected abstract bool StoreContainsKey(TKey key);

    protected abstract bool StoreTryRemove(TKey key);

    protected abstract void StoreClear();


    protected virtual void OnBeforeUnregister(TKey key) { }

    protected virtual void OnBeforeClear() { }


    public bool Contains(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return StoreContainsKey(key);
    }

    public virtual void Unregister(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        OnBeforeUnregister(key);
        StoreTryRemove(key);
    }

    public virtual void Clear()
    {
        OnBeforeClear();
        StoreClear();
    }
}
