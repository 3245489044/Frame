#nullable enable

using System;
using System.Collections.Concurrent;

namespace Share.IdentityFramework;

/// <summary>
/// 按域单调序号分配器（线程安全）。
/// <para>同一 domainKey 内从 <c>firstValue</c> 起单调递增；不同域各自独立计数。
/// 只分配序号，不解释序号语义（事件行序 / 实体计数 / 交付代际均由调用方定义）。</para>
/// </summary>
public interface ISequenceAllocator
{
    /// <summary>分配下一个序号（同一域内单调递增）。</summary>
    long Next(string domainKey);
}

/// <summary>
/// 默认序号分配器：<see cref="ConcurrentDictionary{TKey,TValue}"/> 后端，每域从 1 开始。
/// <para>统一表示选 <see cref="long"/>：跨域合并、诊断与持久化不会在 2^31 处截断；
/// 既有 <c>int</c> 消费方（如观测实体序号）在边界用 <see cref="NextInt32"/> 做显式检查转换。</para>
/// </summary>
public sealed class SequenceAllocator : ISequenceAllocator
{
    private readonly ConcurrentDictionary<string, long> _seq = new(StringComparer.Ordinal);
    private readonly long _firstValue;

    /// <param name="firstValue">每域首个分配值（默认 1）。</param>
    public SequenceAllocator(long firstValue = 1)
    {
        _firstValue = firstValue;
    }

    /// <inheritdoc />
    public long Next(string domainKey)
    {
        if (string.IsNullOrWhiteSpace(domainKey))
            throw new ArgumentException("domainKey 不得为空。", nameof(domainKey));
        return _seq.AddOrUpdate(domainKey, _firstValue, static (_, current) => current + 1);
    }

    /// <summary>分配 <see cref="int"/> 序号：超出 <see cref="int.MaxValue"/> 时抛 <see cref="OverflowException"/>。</summary>
    public int NextInt32(string domainKey)
    {
        var value = Next(domainKey);
        if (value > int.MaxValue)
            throw new OverflowException($"序号域 \"{domainKey}\" 已超出 Int32 范围：{value}。");
        return (int)value;
    }

    /// <summary>当前已分配的最大序号（未分配过的域返回 <c>firstValue - 1</c>）。</summary>
    public long Current(string domainKey)
        => string.IsNullOrWhiteSpace(domainKey)
            ? throw new ArgumentException("domainKey 不得为空。", nameof(domainKey))
            : _seq.TryGetValue(domainKey, out var value) ? value : _firstValue - 1;
}
