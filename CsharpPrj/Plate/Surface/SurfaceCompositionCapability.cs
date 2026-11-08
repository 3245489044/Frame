#nullable enable

namespace UiBackend;

/// <summary>宿主合成通道（决定真玻璃的可达上限；未声明 ⇒ 不得推测能力）。</summary>
public enum SurfaceCompositionMode
{
    /// <summary>无合成通道（只有 GDI 自绘）。</summary>
    None = 0,

    /// <summary>整窗 DWM 材料（系统层模糊，无法逐元素）。</summary>
    WholeWindowDwm = 1,

    /// <summary>分层窗口（逐像素 alpha 上屏，可 capture 桌面）。</summary>
    LayeredWindow = 2,

    /// <summary>DirectComposition（逐元素 GPU 合成）。</summary>
    DirectComposition = 3,

    /// <summary>引擎合成（UI 帧由 Engine 产出，宿主只贴位图）。</summary>
    EngineComposition = 4,
}

/// <summary>上屏 alpha 口径（决定混合式与位图格式转换方向）。</summary>
public enum SurfaceAlphaMode
{
    /// <summary>忽略 alpha（不透明上屏）。</summary>
    Ignore = 0,

    /// <summary>预乘 alpha（SourceOver = One / OneMinusSrcAlpha）。</summary>
    Premultiplied = 1,
}

/// <summary>
/// 宿主合成能力（**唯一事实源**）：由宿主声明，载体只消费。
/// <para>缺省 = <see cref="Unavailable"/>（未声明不得冒充可用）；可达层级由 <see cref="MaxTier"/> 单一推导。</para>
/// </summary>
/// <param name="Mode">合成通道。</param>
/// <param name="AlphaMode">上屏 alpha 口径。</param>
/// <param name="SupportsOwnerDraw">是否允许载体 GDI 自绘（关掉即整窗系统外观）。</param>
/// <param name="SupportsGpuComposition">是否存在 GPU 合成面（DComp/Engine）。</param>
/// <param name="SupportsDesktopBackdrop">是否能取到桌面/窗口后方内容。</param>
/// <param name="Reason">不可用原因码（可用时为空）。</param>
public sealed record SurfaceCompositionCapability(
    SurfaceCompositionMode Mode,
    SurfaceAlphaMode AlphaMode,
    bool SupportsOwnerDraw,
    bool SupportsGpuComposition,
    bool SupportsDesktopBackdrop,
    string Reason = "")
{
    /// <summary>宿主未声明能力（显式 Unknown，不伪造）。</summary>
    public static readonly SurfaceCompositionCapability Unavailable =
        new(SurfaceCompositionMode.None, SurfaceAlphaMode.Ignore, false, false, false, "host-undeclared");

    /// <summary>该宿主可达的玻璃上限。</summary>
    public GlassTier MaxTier => Mode switch
    {
        SurfaceCompositionMode.EngineComposition => GlassTier.T4,
        SurfaceCompositionMode.DirectComposition => GlassTier.T3,
        SurfaceCompositionMode.LayeredWindow => GlassTier.T2,
        SurfaceCompositionMode.WholeWindowDwm => GlassTier.T1,
        SurfaceCompositionMode.None => SupportsOwnerDraw ? GlassTier.T0 : GlassTier.Unsupported,
        _ => GlassTier.Unsupported,
    };
}
