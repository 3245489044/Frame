#nullable enable

using System;
using SurfacePaint.Contracts;
using UiBackend;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 光 + 指针 → 载体窄视图的**唯一投影点**。
/// <para>指针是光源集合里的第二个发射体：有自己的位置/半径/权重，与环境光共存，不再把指针混进归一化 SpotX/SpotY。</para>
/// <para>无光帧 ⇒ null（载体保持声明值，禁伪造）。指针未就绪/窗口外 ⇒ 不追加指针成员。</para>
/// </summary>
public static class SurfaceLightPointer
{
    private static volatile float _weight = 0.35f;
    private static volatile float _sigma = 0.35f;
    private static volatile float _specBoost = 0.45f;
    private static volatile bool _htmlLocalRead;

    /// <summary>指针光峰值权重（声明令牌 <c>light_pointer_weight</c> 单源；0 = 关闭/回滚）。</summary>
    public static float PointerWeight => _weight;

    /// <summary>指针光半径系数（声明令牌 <c>light_pointer_sigma</c>：半径 = σ × 客户区短边）。</summary>
    public static float PointerSigma => _sigma;

    /// <summary>指针高光增幅上限（声明令牌 <c>light_pointer_spec_boost</c>；0 = 关闭）。</summary>
    public static float SpecBoost => _specBoost;

    /// <summary>
    /// HTML 载体本地读指针的**显式豁免开关**（声明令牌 <c>light_pointer_html_local</c>，默认关闭）。
    /// <para>开启后页面内 JS 只读位置；光数学/发布仍由 C# 单源。关闭时不得注册任何页面鼠标监听。</para>
    /// </summary>
    public static bool HtmlLocalRead => _htmlLocalRead;

    /// <summary>兼容旧调用：只配置指针高光增幅。</summary>
    /// <param name="specBoost">增幅（0..1）。</param>
    public static void Configure(float specBoost) => _specBoost = Math.Clamp(specBoost, 0f, 1f);

    /// <summary>配置指针光（声明令牌单源）。</summary>
    /// <param name="weight">峰值权重（0..1；0 = 关闭）。</param>
    /// <param name="sigma">半径系数（0..1；乘以客户区短边）。</param>
    /// <param name="specBoost">高光增幅（0..1）。</param>
    public static void Configure(float weight, float sigma, float specBoost)
    {
        _weight = Math.Clamp(weight, 0f, 1f);
        _sigma = Math.Clamp(sigma, 0f, 1f);
        _specBoost = Math.Clamp(specBoost, 0f, 1f);
    }

    /// <summary>配置 HTML 载体本地读开关（默认 false）。</summary>
    /// <param name="enabled">true = 允许页面内 JS 读位置（显式豁免）。</param>
    public static void ConfigureHtmlLocal(bool enabled) => _htmlLocalRead = enabled;

    /// <summary>在给定覆盖区采样一次，并把指针作为独立发射体追加。</summary>
    /// <param name="frame">光帧（null ⇒ 无光）。</param>
    /// <param name="coverage">采样覆盖区（屏幕 DIP）。</param>
    /// <param name="dpi">采样 DPI。</param>
    /// <param name="pointer">指针输入（窗口客户区 DIP）。</param>
    /// <param name="viewportWidthDip">窗口客户区宽（DIP；&lt;=0 ⇒ 指针权重恒 0）。</param>
    /// <param name="viewportHeightDip">窗口客户区高（DIP）。</param>
    /// <param name="includePointer">false ⇒ 只返回环境光（供只关心环境光的调用点显式排除指针）。</param>
    public static SurfaceLightState? Apply(
        SurfaceLightFrame? frame,
        SurfaceLightCoverage coverage,
        int dpi,
        SurfacePointerState pointer,
        double viewportWidthDip = 0,
        double viewportHeightDip = 0,
        bool includePointer = true)
    {
        // 指针光不依赖环境光帧：frame 为 null 时仍可投影「仅指针」光态（HasValue=false ⇒ 环境层保持声明值）。
        SurfaceLightState state;
        if (frame is null)
        {
            state = SurfaceLightState.Unavailable;
        }
        else
        {
            var probe = new SurfaceLightProbe(coverage.X, coverage.Y, coverage.Width, coverage.Height, ZOrder: 0, Dpi: dpi);
            state = SurfaceLightState.From(SurfaceLightPort.Sample(frame, probe));
            if (!state.HasValue) return state;
        }

        if (!includePointer || _weight <= 0f || !pointer.Inside)
            return frame is null ? null : state with { PointerWeight = 0f };

        var emitter = ResolvePointerEmitter(pointer, coverage, viewportWidthDip, viewportHeightDip, state.TintArgb);
        if (emitter is null) return frame is null ? null : state with { PointerWeight = 0f };

        var envEmitters = state.Emitters;
        var emitters = new SurfaceLightEmitter[(envEmitters?.Count ?? 0) + 1];
        for (var i = 0; i < emitters.Length - 1; i++) emitters[i] = envEmitters![i];
        emitters[^1] = emitter.Value;

        // 指针只提升矩阵镜面权重；光斑中心由独立成员承载，不改写 SpotX/SpotY（历史断点 B-1）。
        // 无环境帧时 Specular 取 1.0：环境层不落笔（HasValue=false），指针自身仍按声明强度可见。
        var specular = frame is null
            ? 1f
            : Math.Clamp(state.Specular * (1f + _specBoost * emitter.Value.Weight), 0f, 1f);
        return state with
        {
            HasValue = frame is not null,
            PointerWeight = emitter.Value.Weight,
            Specular = specular,
            Emitters = emitters,
            LightSignature = frame?.Signature ?? 0L,
        };
    }

    /// <summary>
    /// 载体只读版本：用 registry 已发布的指针成员合成一次（不读 UI 线程指针源）。
    /// <para>HTML 侧固定 1Hz 的投影路径用它；指针位置仍来自 C# 单源发布，不在页面内采样。</para>
    /// </summary>
    public static SurfaceLightState? ApplyPublished(SurfaceLightFrame? frame, SurfaceLightCoverage coverage, int dpi)
    {
        var pointer = SurfaceLightRegistry.Pointer;
        if (pointer is null)
        {
            return Apply(frame, coverage, dpi, SurfacePointerState.Unavailable);
        }

        var client = new SurfacePointerState(
            pointer.Value.ScreenX - coverage.X,
            pointer.Value.ScreenY - coverage.Y,
            true,
            0);
        return Apply(frame, coverage, dpi, client, coverage.Width, coverage.Height);
    }

    /// <summary>
    /// 供宿主把指针发布到 <see cref="SurfaceLightRegistry"/>（屏幕 DIP）。
    /// <para>TintArgb = 0 表示继承环境色；窗口外/关闭 ⇒ null。</para>
    /// </summary>
    /// <param name="pointer">指针状态（客户区 DIP）。</param>
    /// <param name="originX">客户区左上角屏幕 X（DIP）。</param>
    /// <param name="originY">客户区左上角屏幕 Y（DIP）。</param>
    /// <param name="clientWidthDip">客户区宽（DIP）。</param>
    /// <param name="clientHeightDip">客户区高（DIP）。</param>
    public static SurfaceLightEmitter? BuildPointerEmitter(
        SurfacePointerState pointer,
        double originX,
        double originY,
        double clientWidthDip,
        double clientHeightDip)
    {
        if (!pointer.Inside || _weight <= 0f) return null;
        var shortSide = Math.Min(clientWidthDip, clientHeightDip);
        if (shortSide <= 0) return null;

        return new SurfaceLightEmitter(
            SurfaceLightEmitterKind.Pointer,
            (float)(originX + pointer.X),
            (float)(originY + pointer.Y),
            RadiusFrom((float)shortSide),
            _weight,
            0);
    }

    private static SurfaceLightEmitter? ResolvePointerEmitter(
        SurfacePointerState pointer,
        SurfaceLightCoverage coverage,
        double viewportWidthDip,
        double viewportHeightDip,
        int envTintArgb)
    {
        // 分成员发布真值：registry 指针成员存在时只读它，避免两套坐标各算一次。
        if (SurfaceLightRegistry.Pointer is { } published)
        {
            var tint = published.TintArgb == 0 ? envTintArgb : published.TintArgb;
            return published with { TintArgb = tint };
        }

        var shortSide = Math.Min(coverage.Width, coverage.Height);
        if (shortSide <= 0) shortSide = (float)Math.Min(viewportWidthDip, viewportHeightDip);
        if (shortSide <= 0) return null;

        return new SurfaceLightEmitter(
            SurfaceLightEmitterKind.Pointer,
            coverage.X + pointer.X,
            coverage.Y + pointer.Y,
            RadiusFrom(shortSide),
            _weight,
            envTintArgb);
    }

    private static float RadiusFrom(float shortSide) => Math.Max(8f, _sigma * shortSide);
}
