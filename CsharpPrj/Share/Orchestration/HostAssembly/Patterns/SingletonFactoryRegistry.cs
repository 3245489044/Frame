using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Share.HostAssembly.Patterns;

/// <summary>
/// 单实例工厂注册表 —— 按 key 保证同一 key 至多构造一个实例（消除同路径/同资源二次开碰撞），
/// 供资源/连接/协调器等「需按标识符共享单一 owner」的场景复用。
/// 支持两种工厂：构造时固定的默认工厂（<see cref="GetOrCreate(TKey)"/>），或每次调用提供的上下文感知工厂
/// （<see cref="GetOrCreate(TKey, Func{TKey,TValue})"/>，供工厂需携带调用上下文如配置/取消令牌的场景）。
/// </summary>
/// <typeparam name="TKey">实例标识（如文件绝对路径）。</typeparam>
/// <typeparam name="TValue">实例类型（引用类型）。</typeparam>
public sealed class SingletonFactoryRegistry<TKey, TValue>
    where TKey : notnull
    where TValue : class
{
    private readonly ConcurrentDictionary<TKey, TValue> _items;
    private readonly object _gate = new();
    private readonly Func<TKey, TValue>? _factory;
    private readonly Func<TValue, bool>? _isDisposed;

    /// <param name="factory">可选的按 key 构造新实例的默认工厂；不提供时须经 <see cref="GetOrCreate(TKey, Func{TKey,TValue})"/> 传入上下文感知工厂。</param>
    /// <param name="isDisposed">可选的「实例是否已释放」判定；提供时可避免把已释放实例复用给新调用方。</param>
    /// <param name="comparer">key 比较器（缺省按类型默认）。</param>
    public SingletonFactoryRegistry(Func<TKey, TValue>? factory = null, Func<TValue, bool>? isDisposed = null,
        IEqualityComparer<TKey>? comparer = null)
    {
        _factory = factory;
        _isDisposed = isDisposed;
        _items = new ConcurrentDictionary<TKey, TValue>(comparer ?? EqualityComparer<TKey>.Default);
    }

    /// <summary>取或创建 key 对应的实例（使用构造时提供的默认工厂）。锁内查重 + 置位，避免并发重复 Start/重复注册释放。</summary>
    public TValue GetOrCreate(TKey key)
        => GetOrCreate(key, _factory ?? throw new InvalidOperationException(
            "未配置默认工厂，请改用 GetOrCreate(key, factory) 提供上下文感知工厂。"));

    /// <summary>取或创建 key 对应的实例（使用本次调用提供的工厂，工厂可捕获调用上下文如配置/取消令牌）。</summary>
    public TValue GetOrCreate(TKey key, Func<TKey, TValue> factory)
    {
        PortGuard.Required(factory, nameof(factory));
        lock (_gate)
        {
            if (_items.TryGetValue(key, out var existing) && !IsDisposed(existing))
                return existing;
            var created = factory(key);
            _items[key] = created;
            return created;
        }
    }

    private bool IsDisposed(TValue value)
        => _isDisposed is not null && _isDisposed(value);

    /// <summary>当前已注册的 key 集合。</summary>
    public IReadOnlyCollection<TKey> Keys => _items.Keys.ToArray();
}
