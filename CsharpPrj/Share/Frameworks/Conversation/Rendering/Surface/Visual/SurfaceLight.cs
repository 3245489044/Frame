#nullable enable

using System;
using System.Globalization;
using MathematicalAlgorithms.Lighting;
using SurfacePaint.Contracts;
using UiBackend;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 运行时光参数的**载体窄视图**（唯一入口已改为 <see cref="SurfaceLightPort"/>）：
/// <see cref="Angle"/>/<see cref="Intensity"/>/<see cref="Warmth"/> 喂现有 <c>specular</c> 与 <c>backdrop</c>；
/// 其余字段给出同一采样结果的边缘光/光斑/阴影方向，供载体按需使用。
/// <para>本类型不再包含任何光数学（时间表、光标偏转已删除），只做结果投影。</para>
/// </summary>
public readonly record struct SurfaceLightState(float Angle, float Intensity, float Warmth)
{
    /// <summary>是否来自有效光场；false ⇒ 载体保持声明值（禁止回落时间表）。</summary>
    public bool HasValue { get; init; } = true;

    /// <summary>指向光的方向（屏幕空间；x 右、y 下、z 指向观察者）。</summary>
    public float DirectionX { get; init; }

    /// <inheritdoc cref="DirectionX"/>
    public float DirectionY { get; init; }

    /// <inheritdoc cref="DirectionX"/>
    public float DirectionZ { get; init; }

    /// <summary>光斑中心（归一化 0..1）。</summary>
    public float SpotX { get; init; } = 0.5f;

    /// <inheritdoc cref="SpotX"/>
    public float SpotY { get; init; } = 0.5f;

    /// <summary>四边边缘光权重（0..1，最亮边=1）。</summary>
    public float EdgeTop { get; init; }

    /// <inheritdoc cref="EdgeTop"/>
    public float EdgeLeft { get; init; }

    /// <inheritdoc cref="EdgeTop"/>
    public float EdgeRight { get; init; }

    /// <inheritdoc cref="EdgeTop"/>
    public float EdgeBottom { get; init; }

    /// <summary>阴影方向（归一化 -1..1）。</summary>
    public float ShadowX { get; init; }

    /// <inheritdoc cref="ShadowX"/>
    public float ShadowY { get; init; }

    /// <summary>该处环境色（0xAARRGGBB；色值解释统一由 <see cref="SurfaceColor"/> 负责）。</summary>
    public int TintArgb { get; init; }

    /// <summary>该处矩阵镜面权重（0..1）：高光/边缘光强度由它驱动，不再另定人工 strength。</summary>
    public float Specular { get; init; } = 1f;

    /// <summary>世界固定锚点（屏幕 DIP）；静态/动态载体都按它在屏幕上的位置画光。</summary>
    public SurfaceLightAnchor Anchor { get; init; } = new(0f, 0f, 0f, 0f);

    /// <summary>光斑中心相对控件左上角（DIP，非百分比）；控件移动时它同步反向移动，屏幕位置不变。</summary>
    public float SpotLocalX { get; init; }

    /// <inheritdoc cref="SpotLocalX"/>
    public float SpotLocalY { get; init; }

    /// <summary>光斑半径（DIP）。</summary>
    public float SpotRadiusDips { get; init; }

    /// <summary>照度权重（0..1）：超出判定半径即 0。</summary>
    public float Shade { get; init; } = 1f;

    /// <summary>虹彩相位（0..1）：光斑相对控件中心的方位角（世界固定锚点 − 控件中心）。</summary>
    public float IridescencePhase { get; init; }

    /// <summary>指针贡献比重（0..1；0 = 无指针或无指针源，禁伪造）。</summary>
    public float PointerWeight { get; init; }

    /// <summary>光场签名（缓存失效依据）。</summary>
    public long LightSignature { get; init; }

    /// <summary>
    /// 把窗口级光态重投影到**卡片局部坐标**（唯一换算入口）。
    /// <para>锚点是屏幕固定 DIP；卡片移动/嵌套时，光斑局部位置、照度权重与虹彩相位都必须按卡片矩形重算，
    /// 否则同一份窗口级 SpotLocal 会被所有卡片复用（光斑错位、远处卡片仍被照亮）。</para>
    /// </summary>
    /// <param name="originXDip">卡片客户区左上角屏幕 X（DIP）。</param>
    /// <param name="originYDip">卡片客户区左上角屏幕 Y（DIP）。</param>
    /// <param name="widthDip">卡片宽（DIP）。</param>
    /// <param name="heightDip">卡片高（DIP）。</param>
    public SurfaceLightState ForCard(double originXDip, double originYDip, double widthDip, double heightDip)
    {
        if (!HasValue) return this;

        var localX = Anchor.X - (float)originXDip;
        var localY = Anchor.Y - (float)originYDip;
        var spotX = widthDip > 1e-6 ? (float)(localX / widthDip) : SpotX;
        var spotY = heightDip > 1e-6 ? (float)(localY / heightDip) : SpotY;

        var centerX = (float)(originXDip + widthDip * 0.5);
        var centerY = (float)(originYDip + heightDip * 0.5);
        var dx = Anchor.X - centerX;
        var dy = Anchor.Y - centerY;
        var distance = MathF.Sqrt(dx * dx + dy * dy);
        var litRadius = Anchor.LitRadiusDips;
        var shade = litRadius <= 1e-6f
            ? 1f
            : Math.Clamp(1f - (distance / litRadius) * (distance / litRadius), 0f, 1f);

        return this with
        {
            SpotLocalX = localX,
            SpotLocalY = localY,
            SpotX = spotX,
            SpotY = spotY,
            Shade = shade,
            IridescencePhase = IridescenceRamp.Bearing01(dx, dy),
        };
    }

    /// <summary>无光场可用：载体不得据此重画光，只能保持声明值。</summary>
    public static readonly SurfaceLightState Unavailable = new(135f, 0f, 0f) { HasValue = false, Specular = 0f };

    /// <summary>位图缓存键片段（含插值后的光参数；不含秒级噪声；格式与采样令牌同口径，不受区域设置影响）。</summary>
    public string CacheToken => HasValue
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"{Angle:F1}:{Intensity:F2}:{Warmth:F3}:{SpotX:F2}:{SpotY:F2}:{Specular:F2}:P{PointerWeight:F2}:S{LightSignature}")
        : "light-unavailable";

    /// <summary>
    /// **静态底图**缓存键片段：只含位置无关量（方向角/镜面/阴影/签名），
    /// 不含光斑中心与照度权重 ⇒ 面板平移不再使底图失效；光斑/条带/虹彩全走动态层。
    /// </summary>
    public string StaticCacheToken => HasValue
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"{Angle:F1}:{Specular:F2}:{ShadowX:F2}:{ShadowY:F2}:S{LightSignature}")
        : "light-unavailable";

    /// <summary>
    /// 高光峰值 alpha（**两载体唯一公式**）：矩阵镜面权重 × 材质声明 <c>strength</c>（只作上限）× 照度权重
    /// <see cref="Shade"/>，封顶 200。GDI+ 的 <c>SkinPaintEngine.SheenPeak</c> 与 CSS 的
    /// <c>--sl-spot-color</c>/<c>--sl-spectral-a</c> 都必须走这里，禁止各写一份（否则同一份光投出不同亮度）。
    /// </summary>
    /// <param name="baseAlpha">载体基准峰值（GDI+ 光斑 90 / 边缘 150）。</param>
    /// <param name="strength">材质声明强度（0..1，仅作上限）。</param>
    /// <param name="specular">矩阵镜面权重（0..1）。</param>
    /// <param name="shade">照度权重（0..1）。</param>
    public static int SheenAlpha(float baseAlpha, float strength, float specular, float shade)
        => SurfaceShadingMath.SheenPeak(baseAlpha, strength, specular, shade);

    /// <summary>采样结果 → 载体窄视图（**唯一投影点**）。</summary>
    public static SurfaceLightState From(SurfaceLightSample sample)
    {
        if (!sample.HasValue) return Unavailable;

        return new SurfaceLightState(sample.Angle, sample.Intensity, sample.Warmth)
        {
            HasValue = true,
            DirectionX = sample.Direction.X,
            DirectionY = sample.Direction.Y,
            DirectionZ = sample.Direction.Z,
            SpotX = sample.SpotX,
            SpotY = sample.SpotY,
            EdgeTop = sample.EdgeTop,
            EdgeLeft = sample.EdgeLeft,
            EdgeRight = sample.EdgeRight,
            EdgeBottom = sample.EdgeBottom,
            ShadowX = sample.ShadowX,
            ShadowY = sample.ShadowY,
            TintArgb = sample.TintArgb,
            Specular = sample.Specular,
            Anchor = sample.Anchor,
            SpotLocalX = sample.SpotLocalX,
            SpotLocalY = sample.SpotLocalY,
            SpotRadiusDips = sample.SpotRadiusDips,
            Shade = sample.Shade,
            IridescencePhase = sample.IridescencePhase,
            LightSignature = sample.Signature,
        };
    }
}

/// <summary>
/// 运行时光入口（兼容旧调用点）：**不再**按时间/光标近似，只读全局光场
/// <see cref="SurfaceLightRegistry.Current"/>。等价于在整帧中心取一次采样；
/// 需要按组件位置取光时调用 <see cref="SurfaceLightPort.Sample"/>。
/// </summary>
public static class SurfaceLight
{
    /// <summary>取全局帧中心处的光参数（旧签名保留，参数不再参与计算）。</summary>
    /// <param name="localTime">保留参数；不再读取时间。</param>
    /// <param name="cursorX">保留参数；不再做光标偏转。</param>
    /// <param name="width">保留参数；不再做光标偏转。</param>
    public static SurfaceLightState Resolve(DateTime localTime, int cursorX = 0, int width = 0)
    {
        _ = localTime;
        _ = cursorX;
        _ = width;

        var read = SurfaceLightReader.Read();
        if (read.Frame is null) return SurfaceLightState.Unavailable;

        var coverage = read.Frame.Coverage;
        return SurfaceLightPointer.Apply(
                   read.Frame, coverage, 96, SurfacePointerState.Unavailable)
               ?? SurfaceLightState.Unavailable;
    }
}
