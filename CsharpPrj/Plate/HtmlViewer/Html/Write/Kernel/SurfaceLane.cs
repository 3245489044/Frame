#nullable enable

using System;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 写车道（D5）：交互 / 刷新 / 流。语义只做**准入、预算与丢弃策略**——
/// 同一控件仍由单串行通道承载（禁并发写同一 DOM，保护单写者不变量）。
/// </summary>
public enum SurfaceLaneKind
{
    Interactive = 0,
    Refresh,
    Stream,
}

/// <summary>车道映射与策略（表驱动，单一实现）。</summary>
public static class SurfaceLane
{
    /// <summary>来源 → 车道（Tick 归 Stream：只产易变叶子，压力下可丢）。</summary>
    public static SurfaceLaneKind Of(UiWriteSource source) => source switch
    {
        UiWriteSource.Interactive => SurfaceLaneKind.Interactive,
        UiWriteSource.Stream or UiWriteSource.Tick => SurfaceLaneKind.Stream,
        _ => SurfaceLaneKind.Refresh,
    };

    /// <summary>车道是否可丢（仅 stream；丢必须计数可见）。</summary>
    public static bool Droppable(SurfaceLaneKind lane) => lane == SurfaceLaneKind.Stream;

    /// <summary>车道优先级（数值小者优先；与 ControlSerialQueue 来源优先级同序）。</summary>
    public static int Priority(SurfaceLaneKind lane) => (int)lane;
}

/// <summary>
/// 交互预留令牌（滑动窗口）：窗口内交互最多占用 <paramref name="reserveMs"/>；
/// 超限只记账（不阻断），供「交互是否被日志/重建挤占」可判定。
/// </summary>
public sealed class InteractiveReserve
{
    private readonly object _gate = new();
    private readonly int _windowMs;
    private readonly int _reserveMs;
    private long _windowStartMs;
    private int _usedMs;

    public InteractiveReserve(int windowMs = 100, int reserveMs = 12)
    {
        _windowMs = Math.Max(1, windowMs);
        _reserveMs = Math.Max(0, reserveMs);
    }

    /// <summary>尝试占用 <paramref name="ms"/>；窗口滚动后重置。返回 false = 超出预留（调用方只记账）。</summary>
    public bool TryEnter(int ms)
    {
        var now = Environment.TickCount64;
        lock (_gate)
        {
            if (now - _windowStartMs >= _windowMs)
            {
                _windowStartMs = now;
                _usedMs = 0;
            }

            if (_usedMs + Math.Max(0, ms) > _reserveMs) return false;
            _usedMs += Math.Max(0, ms);
            return true;
        }
    }
}
