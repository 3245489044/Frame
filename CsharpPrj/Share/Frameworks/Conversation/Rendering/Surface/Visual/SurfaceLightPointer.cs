#nullable enable

using System;
using SurfacePaint.Contracts;
using UiBackend;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 光 + 指针 → 载体窄视图的**唯一投影点**（阶段一：指针只调权重，不改锚点）。
/// <para>无光帧 ⇒ null（载体保持声明值，禁伪造）。指针未就绪/窗口外 ⇒ 权重 0。</para>
/// </summary>
public static class SurfaceLightPointer
{
    /// <summary>在给定覆盖区采样一次，并叠加指针权重。</summary>
    /// <param name="frame">光帧（null ⇒ 无光）。</param>
    /// <param name="coverage">采样覆盖区（屏幕 DIP）。</param>
    /// <param name="dpi">采样 DPI。</param>
    /// <param name="pointer">指针输入（窗口客户区 DIP）。</param>
    /// <param name="viewportWidthDip">窗口客户区宽（DIP；&lt;=0 ⇒ 指针权重恒 0）。</param>
    /// <param name="viewportHeightDip">窗口客户区高（DIP）。</param>
    public static SurfaceLightState? Apply(
        SurfaceLightFrame? frame,
        SurfaceLightCoverage coverage,
        int dpi,
        SurfacePointerState pointer,
        double viewportWidthDip = 0,
        double viewportHeightDip = 0)
    {
        if (frame is null) return null;

        var probe = new SurfaceLightProbe(coverage.X, coverage.Y, coverage.Width, coverage.Height, ZOrder: 0, Dpi: dpi);
        var state = SurfaceLightState.From(SurfaceLightPort.Sample(frame, probe));
        if (!state.HasValue) return state;

        var weight = Weight(pointer, viewportWidthDip, viewportHeightDip);
        return weight <= 0f ? state with { PointerWeight = 0f } : state with { PointerWeight = weight };
    }

    private static float Weight(SurfacePointerState pointer, double width, double height)
    {
        if (!pointer.Inside || width <= 0 || height <= 0) return 0f;
        // 距离窗口中心归一化：中心=1，四角=0（阶段一只作强度调制，不改锚点）。
        var cx = width / 2.0;
        var cy = height / 2.0;
        var dx = (pointer.X - cx) / Math.Max(cx, 1.0);
        var dy = (pointer.Y - cy) / Math.Max(cy, 1.0);
        var distance = Math.Sqrt(dx * dx + dy * dy); // 0..~1.41
        return (float)Math.Clamp(1.0 - distance / 1.5, 0.0, 1.0);
    }
}
