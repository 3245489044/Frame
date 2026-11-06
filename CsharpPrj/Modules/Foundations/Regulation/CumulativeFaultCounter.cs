using System.Collections.Concurrent;

namespace Modules.Regulation;

/// <summary>
/// 跨周期/跨补位保留的累计故障计数器（显式重置）。
/// <para>
/// 与 <see cref="ProgressTracker"/> 的差异：<see cref="ProgressTracker"/> 在“观测值出现进展”时自动重置，
/// 本计数器只在显式 <see cref="Reset(string)"/> / <see cref="ResetAll"/> 时清零，用于“同一资源反复失败”
/// 的累计归因（如 worker 超时次数、重试配额消耗）。
/// </para>
/// </summary>
public sealed class CumulativeFaultCounter
{
    private readonly ConcurrentDictionary<string, long> _counts = new(StringComparer.Ordinal);

    /// <summary>累加一次并返回累加后的值。</summary>
    public long Increment(string key, long delta = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _counts.AddOrUpdate(key, delta, (_, existing) => existing + delta);
    }

    /// <summary>读取当前累计值（不存在返回 0）。</summary>
    public long Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _counts.TryGetValue(key, out var value) ? value : 0L;
    }

    /// <summary>读取当前累计值。</summary>
    public bool TryGet(string key, out long value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _counts.TryGetValue(key, out value);
    }

    /// <summary>显式清零单个键。</summary>
    public void Reset(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _counts.TryRemove(key, out _);
    }

    /// <summary>清空全部累计（池级重建/换代时调用）。</summary>
    public void ResetAll() => _counts.Clear();

    /// <summary>已跟踪键数。</summary>
    public int Count => _counts.Count;
}
