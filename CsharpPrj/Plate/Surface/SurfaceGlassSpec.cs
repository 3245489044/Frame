#nullable enable

using System;

namespace UiBackend;

/// <summary>真玻璃分层（T0 页内模糊 → T4 3D 场景玻璃）；<see cref="Unsupported"/> = 未达层。</summary>
public enum GlassTier
{
    /// <summary>未达任何玻璃层（不得伪装）。</summary>
    Unsupported = -1,

    /// <summary>页内真模糊（内容自身可被模糊）。</summary>
    T0 = 0,

    /// <summary>整窗 DWM 材料。</summary>
    T1 = 1,

    /// <summary>快照毛玻璃（父级/窗口快照 + 模糊）。</summary>
    T2 = 2,

    /// <summary>逐像素桌面透明（分层窗口 + 捕获）。</summary>
    T3 = 3,

    /// <summary>3D 场景玻璃（引擎合成）。</summary>
    T4 = 4,
}

/// <summary>未达层时的显式降级形态。</summary>
public enum SurfaceGlassFallback
{
    /// <summary>不透明面（无玻璃）。</summary>
    Opaque = 0,

    /// <summary>声明式近似（渐变/丝带，**不是**真玻璃）。</summary>
    Emulated = 1,

    /// <summary>不可渲染（调用方必须记账并跳过）。</summary>
    Unsupported = 2,
}

/// <summary>
/// 玻璃规格（**选层结论**）：层级由「合成能力 ∩ 背景源 ∩ 预算 ∩ 声明上限」取最小。
/// <para>参数只描述形态上限，光由 <see cref="SurfaceLightFrame"/> 唯一提供。</para>
/// </summary>
/// <param name="Tier">生效层。</param>
/// <param name="BlurSigma">模糊半径（DIP；0 = 不模糊）。</param>
/// <param name="RefractStrength">折射强度（0..1）。</param>
/// <param name="Dispersion">色散（0..1）。</param>
/// <param name="FresnelStrength">菲涅尔边缘强度（0..1）。</param>
/// <param name="TintArgb">玻璃染色（0xAARRGGBB）。</param>
/// <param name="Fallback">未达层时的降级形态。</param>
/// <param name="Reason">选层原因码（降级/钳制可追溯）。</param>
public sealed record SurfaceGlassSpec(
    GlassTier Tier,
    float BlurSigma,
    float RefractStrength,
    float Dispersion,
    float FresnelStrength,
    int TintArgb,
    SurfaceGlassFallback Fallback,
    string Reason = "")
{
    /// <summary>不透明面（未达层）。</summary>
    public static readonly SurfaceGlassSpec Opaque = new(
        GlassTier.Unsupported, 0f, 0f, 0f, 0f, unchecked((int)0xFF000000),
        SurfaceGlassFallback.Opaque, "glass-unsupported");

    /// <summary>是否有真模糊（≥T0 且半径 &gt; 0）。</summary>
    public bool SupportsBlur => Tier >= GlassTier.T0 && BlurSigma > 0f;

    /// <summary>
    /// 选层（唯一入口）：tier = min(声明, 合成上限, 背景源上限, 预算上限)；低于 T0 ⇒ 显式降级。
    /// </summary>
    /// <param name="composition">宿主合成能力。</param>
    /// <param name="backdrop">背后内容源。</param>
    /// <param name="declared">材质声明的层级上限（<c>null</c> = 未声明，与「不可用」区分）。</param>
    /// <param name="budget">帧预算（可空 ⇒ 不参与钳制）。</param>
    public static SurfaceGlassSpec Clamp(
        SurfaceCompositionCapability? composition,
        SurfaceBackdropFrame? backdrop,
        GlassTier? declared,
        SurfaceFrameBudget? budget)
        => Clamp(composition, backdrop, declared, budget, SurfaceGlassLookTable.BuiltIn);

    /// <summary>
    /// 选层（声明值版）：形态参数从 <paramref name="look"/> 读取，本类型不再按 tier 硬编码。
    /// </summary>
    /// <param name="composition">宿主合成能力。</param>
    /// <param name="backdrop">背后内容源。</param>
    /// <param name="declared">材质声明的层级上限（<c>null</c> = 未声明，与「不可用」区分）。</param>
    /// <param name="budget">帧预算（可空 ⇒ 不参与钳制）。</param>
    /// <param name="look">玻璃形态声明表（<c>null</c> ⇒ 未装配，显式降级）。</param>
    public static SurfaceGlassSpec Clamp(
        SurfaceCompositionCapability? composition,
        SurfaceBackdropFrame? backdrop,
        GlassTier? declared,
        SurfaceFrameBudget? budget,
        SurfaceGlassLookTable? look)
    {
        if (composition is null)
        {
            return Opaque with { Reason = "composition-missing" };
        }

        if (declared is null)
        {
            // Unknown ≠ NotApplicable：材质未声明层级不得用 T0 兜底（E-12）。
            return Opaque with { Reason = "glass-undeclared" };
        }

        var source = backdrop ?? SurfaceBackdropFrame.Unavailable;
        var limit = composition.MaxTier;
        if (budget is not null && budget.MaxTier < limit) limit = budget.MaxTier;
        if (source.MaxTier < limit) limit = source.MaxTier;

        var declaredTier = declared.Value;
        var tier = declaredTier < limit ? declaredTier : limit;
        if (tier < GlassTier.T0)
        {
            return Opaque with
            {
                Reason = "tier-unavailable:" + Value(composition.Reason) + "/" + Value(source.Reason),
            };
        }

        var table = look ?? SurfaceGlassLookTable.Empty;
        if (!table.TryGet(tier, out var layer))
        {
            return Opaque with { Reason = "glass-look-undeclared:" + tier };
        }

        return new SurfaceGlassSpec(
            tier,
            layer.BlurSigma,
            layer.RefractStrength,
            layer.Dispersion,
            layer.FresnelStrength,
            table.TintArgb,
            SurfaceGlassFallback.Opaque,
            tier < declaredTier ? "tier-clamped" : string.Empty);
    }

    private static string Value(string? text) => string.IsNullOrWhiteSpace(text) ? "-" : text;
}
