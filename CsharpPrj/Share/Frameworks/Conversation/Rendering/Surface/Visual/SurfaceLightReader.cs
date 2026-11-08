#nullable enable

using System;
using UiBackend;

namespace Share.Conversation.Rendering.Surface;

/// <summary>光帧读取结果（帧 + 原因码；禁把「没有帧」压成 null 无声出口）。</summary>
/// <param name="Frame">光帧（不可用时为 null）。</param>
/// <param name="Reason">不可用原因码（可用时为空）。</param>
public readonly record struct SurfaceLightRead(SurfaceLightFrame? Frame, string Reason)
{
    /// <summary>是否有可用光帧。</summary>
    public bool HasFrame => Frame is not null;
}

/// <summary>
/// 光帧读取的**唯一消费者入口**（D-1）：所有载体经此取帧，禁各处直读 registry。
/// <para>当前传输仍为进程内 registry（Gather 未装配时回退路径），原因码显式可见。</para>
/// </summary>
public static class SurfaceLightReader
{
    private static string _lastReason = "not-read";

    /// <summary>最近一次读取的原因码。</summary>
    public static string LastReason => _lastReason;

    /// <summary>读一帧光（唯一 registry 读取点）。</summary>
    public static SurfaceLightRead Read()
    {
        try
        {
            var frame = SurfaceLightRegistry.Current;
            if (frame is null)
            {
                _lastReason = "engine-frame-unavailable";
                return new SurfaceLightRead(null, _lastReason);
            }

            // E-17：超过存活窗口的帧不得继续冒充当前光。
            if (SurfaceLightRegistry.IsStale(3000))
            {
                _lastReason = "light-stale";
                return new SurfaceLightRead(null, _lastReason);
            }

            _lastReason = string.Empty;
            return new SurfaceLightRead(frame, _lastReason);
        }
        catch (Exception ex)
        {
            _lastReason = "light-source-failed:" + ex.GetType().Name;
            return new SurfaceLightRead(null, _lastReason);
        }
    }

    /// <summary>取当前帧（供 Gather producer 使用；无帧 ⇒ null）。</summary>
    public static SurfaceLightFrame? CurrentFrame => SurfaceLightRegistry.Current;
}
