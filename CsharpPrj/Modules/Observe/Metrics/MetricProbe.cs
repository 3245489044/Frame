using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Modules.Observe.Metrics;

/// <summary>
/// 采集入口：关闭态只做一次静态布尔判断并返回 <c>default</c> 句柄；开启态返回内联探针。
/// 埋点形态只有三种：<c>using var _ = Metrics.Timer(id);</c> / <c>Metrics.Counter(id).Add(n);</c> / <c>Metrics.Gauge(id).Set(v);</c>
/// </summary>
public static class Metrics
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MetricProbe Counter(MetricId id) =>
        MetricSession.Enabled ? new MetricProbe(id) : default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MetricProbe Gauge(MetricId id) =>
        MetricSession.Enabled ? new MetricProbe(id) : default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MetricScope Timer(MetricId id) =>
        MetricSession.Enabled ? MetricScope.Start(id) : default;
}

/// <summary>
/// 内联探针句柄：关闭时是 <c>default</c>（Active=false），Add/Set 首行单分支短路，
/// 不分配、不写内存、不触碰字符串/字典/锁。
/// </summary>
public readonly struct MetricProbe
{
    private readonly MetricId _id;
    private readonly bool _active;

    internal MetricProbe(MetricId id)
    {
        _id = id;
        _active = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(long value)
    {
        if (!_active) return;
        MetricSession.AddCounter(_id, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(long value)
    {
        if (!_active) return;
        MetricSession.SetGauge(_id, value);
    }
}

/// <summary>
/// using 计时作用域：开启时在 Dispose 记录一次时间戳差；关闭时 Dispose 立即返回。
/// </summary>
public readonly struct MetricScope : IDisposable
{
    private readonly MetricId _id;
    private readonly long _start;
    private readonly bool _active;

    private MetricScope(MetricId id)
    {
        _id = id;
        _start = Stopwatch.GetTimestamp();
        _active = true;
    }

    internal static MetricScope Start(MetricId id) => new(id);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (!_active) return;
        MetricSession.Record(_id, Stopwatch.GetTimestamp() - _start);
    }
}
