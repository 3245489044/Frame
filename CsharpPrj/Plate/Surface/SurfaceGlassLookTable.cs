#nullable enable

using System;
using System.Collections.Generic;

namespace UiBackend;

/// <summary>某一玻璃层的形态参数（声明值；光仍由 <see cref="SurfaceLightFrame"/> 唯一提供）。</summary>
/// <param name="BlurSigma">模糊半径（DIP；0 = 不模糊）。</param>
/// <param name="RefractStrength">折射强度（0..1）。</param>
/// <param name="Dispersion">色散（0..1）。</param>
/// <param name="FresnelStrength">菲涅尔边缘强度（0..1）。</param>
public readonly record struct SurfaceGlassLayer(
    float BlurSigma,
    float RefractStrength,
    float Dispersion,
    float FresnelStrength);

/// <summary>
/// 玻璃形态声明表（tier → 参数 + 染色）：**唯一值源**是 <c>appearance.ushar</c> 的
/// <c>glass_*</c> 令牌；本类型只承载装配期读取结果，不做档位硬编码。
/// </summary>
public sealed class SurfaceGlassLookTable
{
    private readonly IReadOnlyDictionary<GlassTier, SurfaceGlassLayer> _layers;

    /// <summary>空表：未装配声明 ⇒ 选层显式降级（不得伪造玻璃形态）。</summary>
    public static readonly SurfaceGlassLookTable Empty = new(
        new Dictionary<GlassTier, SurfaceGlassLayer>(), unchecked((int)0xFF000000));

    /// <summary>
    /// 历史内置形态（仅用于无声明装配的纯函数调用/单元契约；生产装配必须传入声明表）。
    /// </summary>
    public static readonly SurfaceGlassLookTable BuiltIn = new(
        new Dictionary<GlassTier, SurfaceGlassLayer>
        {
            [GlassTier.T0] = new(8f, 0f, 0f, 0.35f),
            [GlassTier.T1] = new(12f, 0f, 0f, 0.45f),
            [GlassTier.T2] = new(18f, 0.35f, 0f, 0.55f),
            [GlassTier.T3] = new(24f, 0.55f, 0.25f, 0.65f),
            [GlassTier.T4] = new(32f, 0.75f, 0.45f, 0.75f),
        },
        unchecked((int)0xFFDCEBFF));

    /// <summary>构造声明表。</summary>
    /// <param name="layers">tier → 形态。</param>
    /// <param name="tintArgb">玻璃染色（0xAARRGGBB）。</param>
    public SurfaceGlassLookTable(
        IReadOnlyDictionary<GlassTier, SurfaceGlassLayer> layers,
        int tintArgb)
    {
        _layers = layers ?? throw new ArgumentNullException(nameof(layers));
        TintArgb = tintArgb;
    }

    /// <summary>玻璃染色（0xAARRGGBB）。</summary>
    public int TintArgb { get; }

    /// <summary>是否装配了声明形态。</summary>
    public bool IsDeclared => _layers.Count > 0;

    /// <summary>取某层形态（未声明 ⇒ false）。</summary>
    /// <param name="tier">玻璃层。</param>
    /// <param name="layer">形态。</param>
    public bool TryGet(GlassTier tier, out SurfaceGlassLayer layer)
        => _layers.TryGetValue(tier, out layer);
}
