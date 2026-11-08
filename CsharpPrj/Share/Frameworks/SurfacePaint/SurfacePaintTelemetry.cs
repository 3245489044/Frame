#nullable enable

using System;

namespace Share.SurfacePaint;

/// <summary>
/// 绘制成本采样钩子（**载体无关**）：Begin/RecordLayer/RecordBlur 由宿主注入。
/// <para>默认全为 no-op；<see cref="Current"/> 在一次 <see cref="SurfacePaintEngine.Paint"/> 调用内生效。</para>
/// </summary>
public sealed class SurfacePaintTelemetry
{
    /// <summary>空采样器（不产生 Stopwatch 调用）。</summary>
    public static readonly SurfacePaintTelemetry None =
        new(static () => 0L, static (_, _) => { }, static _ => { });

    [ThreadStatic]
    private static SurfacePaintTelemetry? _current;

    private readonly Func<long> _begin;
    private readonly Action<string, long> _recordLayer;
    private readonly Action<long> _recordBlur;

    /// <summary>构造采样钩子。</summary>
    /// <param name="begin">计时起点（关闭采样返回 0）。</param>
    /// <param name="recordLayer">记录一个图元的绘制耗时。</param>
    /// <param name="recordBlur">记录一次快照模糊耗时。</param>
    public SurfacePaintTelemetry(Func<long> begin, Action<string, long> recordLayer, Action<long> recordBlur)
    {
        _begin = begin ?? throw new ArgumentNullException(nameof(begin));
        _recordLayer = recordLayer ?? throw new ArgumentNullException(nameof(recordLayer));
        _recordBlur = recordBlur ?? throw new ArgumentNullException(nameof(recordBlur));
    }

    /// <summary>当前线程生效的采样器（未设置 ⇒ <see cref="None"/>）。</summary>
    public static SurfacePaintTelemetry Current => _current ?? None;

    /// <summary>计时起点。</summary>
    public long Begin() => _begin();

    /// <summary>记录一个图元的绘制耗时。</summary>
    public void RecordLayer(string primitive, long begin) => _recordLayer(primitive, begin);

    /// <summary>记录一次快照模糊耗时。</summary>
    public void RecordBlur(long begin) => _recordBlur(begin);

    internal static SurfacePaintTelemetry Push(SurfacePaintTelemetry telemetry)
    {
        var previous = _current;
        _current = telemetry;
        return previous ?? None;
    }

    internal static void Pop(SurfacePaintTelemetry previous) => _current = previous;
}
