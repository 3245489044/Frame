using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Pipeline;

/// <summary>
/// 异步策略端口（与同步 <see cref="IStrategy{TInput,TOutput}"/> 同族）：一次按名解析 + 一次异步执行。
/// </summary>
/// <typeparam name="TInput">输入载荷。</typeparam>
/// <typeparam name="TOutput">输出结果。</typeparam>
public interface IAsyncStrategy<in TInput, TOutput>
{
    /// <summary>策略名（诊断/审计用）。</summary>
    string Name { get; }

    /// <summary>执行一次（调用方负责取消与超时）。</summary>
    Task<TOutput> ExecuteAsync(TInput input, CancellationToken ct = default);
}

/// <summary>
/// 异步策略注册表（线程安全）：按 key 解析 <see cref="IAsyncStrategy{TInput,TOutput}"/> 并执行。
/// <para>与 <see cref="StrategyRegistry{TKey,TInput,TOutput}"/> 同构，仅执行显式异步；
/// 供单步 handler 等“一次调用 = 一次异步副作用”的注册/派发场景复用。</para>
/// </summary>
/// <typeparam name="TKey">注册键（通常为枚举或字符串）。</typeparam>
/// <typeparam name="TInput">输入载荷。</typeparam>
/// <typeparam name="TOutput">输出结果。</typeparam>
public sealed class AsyncStrategyRegistry<TKey, TInput, TOutput>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, IAsyncStrategy<TInput, TOutput>> _strategies = new();

    /// <summary>登记（同 key 已存在 ⇒ 抛，禁止静默覆盖）。</summary>
    public void Register(TKey key, IAsyncStrategy<TInput, TOutput> strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        if (!_strategies.TryAdd(key, strategy))
            throw new InvalidOperationException($"Strategy for key '{key}' is already registered.");
    }

    /// <summary>登记或覆盖（装配幂等场景）。</summary>
    public void RegisterOrUpdate(TKey key, IAsyncStrategy<TInput, TOutput> strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        _strategies[key] = strategy;
    }

    /// <summary>按 key 取策略；未登记 ⇒ false。</summary>
    public bool TryGet(TKey key, out IAsyncStrategy<TInput, TOutput>? strategy)
        => _strategies.TryGetValue(key, out strategy);

    /// <summary>按 key 取策略；未登记 ⇒ <see cref="KeyNotFoundException"/>。</summary>
    public IAsyncStrategy<TInput, TOutput> Get(TKey key)
        => _strategies.TryGetValue(key, out var strategy)
            ? strategy
            : throw new KeyNotFoundException($"No strategy found for key '{key}'.");

    /// <summary>按 key 执行一次。</summary>
    public Task<TOutput> ExecuteAsync(TKey key, TInput input, CancellationToken ct = default)
        => Get(key).ExecuteAsync(input, ct);

    /// <summary>是否已登记。</summary>
    public bool ContainsKey(TKey key) => _strategies.ContainsKey(key);

    /// <summary>移除（未登记 ⇒ false）。</summary>
    public bool Remove(TKey key) => _strategies.TryRemove(key, out _);

    /// <summary>清空全部登记。</summary>
    public void Clear() => _strategies.Clear();

    /// <summary>已登记数量。</summary>
    public int Count => _strategies.Count;

    /// <summary>全部已登记键。</summary>
    public IEnumerable<TKey> Keys => _strategies.Keys;
}
