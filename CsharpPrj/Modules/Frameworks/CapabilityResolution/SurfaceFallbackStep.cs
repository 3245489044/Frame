#nullable enable

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
