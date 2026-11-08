#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Modules.CapabilityResolution;

/// <summary>
/// 材质回落梯度（L0–L5）：数值越大越远离玻璃。属性通道最低允许 <see cref="L2SolidFace"/>；
/// <see cref="L3ParentCompositeFace"/> 及以下只能用于显式 NonGlass/Unsupported 族。
/// </summary>
public enum SurfaceFallbackStep
{
    /// <summary>L0：完整层栈（含 backdrop/specular）；draw 通道且无降级。</summary>
    L0Full = 0,

    /// <summary>L1：保留 fill/edge，仅失去真模糊/高光；draw 通道降级。</summary>
    L1Emulated = 1,

    /// <summary>L2：fill 预合成成 A=255 实心玻璃面（属性通道的最低档）。</summary>
    L2SolidFace = 2,

    /// <summary>L3：取父级 SurfaceFace 的代表色（无自身材质时的最低玻璃档）。</summary>
    L3ParentCompositeFace = 3,

    /// <summary>L4：仅旧样式 accent 作为 tint（有业务色、无材质）。</summary>
    L4AccentTintFace = 4,

    /// <summary>L5：无法进入任何肌理，保留原生/系统面；仅 NonGlass/Unsupported 允许。</summary>
    L5Unsupported = 5,
}

/// <summary>
/// 回落梯度判据（**唯一实现**）：实心面识别 + 各通道 L0–L5 映射。
/// <para>
/// 解析内核（<see cref="LayeredResolver"/>）与声明适配层（<c>SurfaceResolver</c>）都必须走本类：
/// 同一层栈、同一通道在两条入口上必须得到相同梯度，禁止各写一份级联。
/// </para>
/// </summary>
public static class SurfaceFallbackStepPolicy
{
    /// <summary>层栈是否含可承载的实心面（<c>fill</c>/<c>gradient</c>）。</summary>
    /// <param name="layers">已解析层栈。</param>
    public static bool HasSolidFace(IReadOnlyList<ResolvedLayer> layers)
        => layers.Any(layer =>
            string.Equals(layer.Primitive, "fill", StringComparison.Ordinal)
            || string.Equals(layer.Primitive, "gradient", StringComparison.Ordinal));

    /// <summary>有实心面 ⇒ <see cref="SurfaceFallbackStep.L2SolidFace"/>，否则 <see cref="SurfaceFallbackStep.L5Unsupported"/>。</summary>
    /// <param name="hasSolidFace">实心面判据结果。</param>
    public static SurfaceFallbackStep SolidFaceOrUnsupported(bool hasSolidFace)
        => hasSolidFace ? SurfaceFallbackStep.L2SolidFace : SurfaceFallbackStep.L5Unsupported;

    /// <summary>属性通道的实心面档（不区分是否降级：fill 已在宿主预合成）。</summary>
    /// <param name="layers">生效层栈。</param>
    public static SurfaceFallbackStep PropertySolidFace(IReadOnlyList<ResolvedLayer> layers)
        => SolidFaceOrUnsupported(HasSolidFace(layers));

    /// <summary>
    /// draw 通道：未回退 ⇒ 降级 <see cref="SurfaceFallbackStep.L1Emulated"/> / 完整
    /// <see cref="SurfaceFallbackStep.L0Full"/>；回退 ⇒ 实心面 <see cref="SurfaceFallbackStep.L2SolidFace"/>。
    /// </summary>
    /// <param name="degraded">是否发生降级。</param>
    /// <param name="fallbackUsed">实际生效的回退名（null = 未回退）。</param>
    public static SurfaceFallbackStep Draw(bool degraded, string? fallbackUsed)
        => fallbackUsed is null
            ? (degraded ? SurfaceFallbackStep.L1Emulated : SurfaceFallbackStep.L0Full)
            : SurfaceFallbackStep.L2SolidFace;

    /// <summary>
    /// 属性通道的页面/宿主槽：未回退 ⇒ 实心面档；回退 ⇒ 取父级 SurfaceFace 代表色
    /// <see cref="SurfaceFallbackStep.L3ParentCompositeFace"/>。
    /// </summary>
    /// <param name="fallbackUsed">实际生效的回退名（null = 未回退）。</param>
    /// <param name="layers">生效层栈（用于实心面判据）。</param>
    public static SurfaceFallbackStep PropertyPanel(string? fallbackUsed, IReadOnlyList<ResolvedLayer> layers)
        => fallbackUsed is null ? PropertySolidFace(layers) : SurfaceFallbackStep.L3ParentCompositeFace;
}
