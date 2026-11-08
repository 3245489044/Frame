#nullable enable

using System;
using System.Collections.Generic;

namespace Share.SurfacePaint.Hosting;

/// <summary>
/// 窗口帧快照存储（每窗口最新 + 上一代）：供绘制期间旧代仍可安全引用。
/// <para>所有权：快照内的引擎位图由帧泵持有（借用），本存储只管理引用，不 Dispose 借用帧。</para>
/// <para>零宿主依赖（不引 Share.Composition）：存储语义自足，Form 宿主可再包一层。</para>
/// </summary>
public sealed class SurfaceFrameStore
{
    private readonly Dictionary<string, SurfaceFrameSnapshot> _latest = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SurfaceFrameSnapshot> _previous = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private long _published;
    private long _released;

    /// <summary>发布次数（诊断）。</summary>
    public long Published => System.Threading.Volatile.Read(ref _published);

    /// <summary>释放次数（诊断）。</summary>
    public long Released => System.Threading.Volatile.Read(ref _released);

    /// <summary>最新快照（未生产 ⇒ null）。</summary>
    public SurfaceFrameSnapshot? Read(string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId)) return null;
        lock (_gate)
        {
            return _latest.TryGetValue(windowId, out var snapshot) ? snapshot : null;
        }
    }

    /// <summary>发布新快照：旧最新转为「上一代」，更早代直接丢弃引用（借用位图由泵释放）。</summary>
    public void Publish(SurfaceFrameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            if (_latest.TryGetValue(snapshot.WindowId, out var current))
            {
                _previous[snapshot.WindowId] = current;
            }

            _latest[snapshot.WindowId] = snapshot;
        }

        System.Threading.Interlocked.Increment(ref _published);
    }

    /// <summary>消费完成后释放早于 <paramref name="generation"/> 的上一代引用（幂等）。</summary>
    public bool Release(string windowId, long generation)
    {
        if (string.IsNullOrWhiteSpace(windowId)) return false;
        lock (_gate)
        {
            if (!_previous.TryGetValue(windowId, out var previous)) return false;
            if (previous.Generation >= generation) return false;
            _previous.Remove(windowId);
        }

        System.Threading.Interlocked.Increment(ref _released);
        return true;
    }

    /// <summary>清空（卸载/测试）。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _previous.Clear();
        }
    }
}
