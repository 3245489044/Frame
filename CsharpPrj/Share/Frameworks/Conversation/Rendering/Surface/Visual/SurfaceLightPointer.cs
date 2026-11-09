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
    private static volatile float _specBoost = 0.45f;

    /// <summary>
    /// 指针高光增幅上限（声明令牌 <c>light_pointer_spec_boost</c> 单源；0 = 关闭）。
    /// </summary>
    /// <param name="specBoost">增幅（0..1）。</param>
    public static void Configure(float specBoost) => _specBoost = Math.Clamp(specBoost, 0f, 1f);

    /// <summary>当前指针高光增幅。</summary>
    public static float SpecBoost => _specBoost;
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
        if (weight <= 0f) return state with { PointerWeight = 0f };

        // B5-3：指针只调强度（矩阵镜面权重乘性增幅，封顶 1）；weight=0 逐位不变。
        var specular = Math.Clamp(state.Specular * (1f + _specBoost * weight), 0f, 1f);

        // B6-5：阶段二把光斑中心向指针位置偏移（屏幕位置稳定，不改锚点语义）。
        var spotX = state.SpotX;
        var spotY = state.SpotY;
        if (viewportWidthDip > 0 && viewportHeightDip > 0)
        {
            var targetX = (float)Math.Clamp(pointer.X / viewportWidthDip, 0.0, 1.0);
            var targetY = (float)Math.Clamp(pointer.Y / viewportHeightDip, 0.0, 1.0);
            var blend = weight * 0.5f;
            spotX = state.SpotX + (targetX - state.SpotX) * blend;
            spotY = state.SpotY + (targetY - state.SpotY) * blend;
        }

        return state with { PointerWeight = weight, Specular = specular, SpotX = spotX, SpotY = spotY };
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
