using System;
using System.Collections.Generic;

namespace Share.HostAssembly.Concurrency;

/// <summary>
/// 键控槽注册表（通用件）—— 按 key 取用（首次创建并缓存）或显式登记实例，消除各宿主复制
/// 「字典 + 锁 + 空 key 校验」的样板；key 比较大小写不敏感。
/// </summary>
/// <typeparam name="T">槽位承载类型（引用类型）。</typeparam>
public sealed class KeyedSlotRegistry<T> where T : class
{
    private readonly object _gate = new();
    private readonly Dictionary<string, T> _slots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<T>? _factory;

    /// <param name="factory">首次取用时创建实例的工厂；null ⇒ <see cref="For"/> 对未登记 key 抛（要求先 <see cref="Register"/>）。</param>
    public KeyedSlotRegistry(Func<T>? factory = null) => _factory = factory;

    /// <summary>取（或创建）指定 key 的实例。key 不可为空。</summary>
    public T For(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("槽位 key 不可为空", nameof(key));
        lock (_gate)
        {
            if (_slots.TryGetValue(key, out var existing)) return existing;
            if (_factory is null)
                throw new KeyNotFoundException($"槽位未登记: {key}");
            var created = _factory();
            _slots[key] = created;
            return created;
        }
    }

    /// <summary>登记指定 key 的实例（幂等：覆盖式）。</summary>
    public void Register(string key, T slot)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("槽位 key 不可为空", nameof(key));
        ArgumentNullException.ThrowIfNull(slot);
        lock (_gate) _slots[key] = slot;
    }

    /// <summary>尝试取已登记实例（不创建）。</summary>
    public bool TryGet(string key, out T slot)
    {
        lock (_gate) return _slots.TryGetValue(key ?? string.Empty, out slot!);
    }
}
