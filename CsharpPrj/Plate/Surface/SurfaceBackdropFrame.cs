#nullable enable

namespace UiBackend;

/// <summary>背后内容来源（玻璃的「真」取决于它，不取决于填充色）。</summary>
public enum SurfaceBackdropKind
{
    /// <summary>无来源（显式不可用）。</summary>
    None = 0,

    /// <summary>页内内容（HTML <c>backdrop-filter</c> 的定位区）。</summary>
    PageContent = 1,

    /// <summary>父级表面快照（WinForms 子树截图）。</summary>
    ParentSnapshot = 2,

    /// <summary>屏幕捕获（逐像素桌面透明）。</summary>
    ScreenCapture = 3,

    /// <summary>引擎背景纹理（离屏 RT）。</summary>
    EngineTexture = 4,

    /// <summary>引擎场景（3D 场景玻璃）。</summary>
    EngineScene = 5,
}

/// <summary>
/// 玻璃背后的内容源（**单一生产者**）：覆盖区/DPI/原点/版本 + 可选句柄。
/// <para>缺省 = <see cref="Unavailable"/>；<see cref="Available"/> 为 false 时调用方必须显式降级，禁以填充冒充。</para>
/// </summary>
/// <param name="Kind">来源种类。</param>
/// <param name="Coverage">覆盖的 UI 空间（屏幕 DIP）。</param>
/// <param name="Dpi">来源 DPI（坐标换算唯一依据）。</param>
/// <param name="OriginX">来源原点 X（屏幕 DIP）。</param>
/// <param name="OriginY">来源原点 Y（屏幕 DIP）。</param>
/// <param name="Version">内容版本（缓存键；内容变化必须递增）。</param>
/// <param name="Handle">载体私有句柄（位图/纹理；跨层传递不解释）。</param>
/// <param name="Reason">不可用原因码。</param>
/// <param name="Detail">结构事实明细（诊断用）。</param>
public sealed record SurfaceBackdropFrame(
    SurfaceBackdropKind Kind,
    SurfaceLightCoverage Coverage,
    int Dpi,
    float OriginX,
    float OriginY,
    long Version,
    object? Handle = null,
    string Reason = "",
    string Detail = "")
{
    /// <summary>是否有真实内容源。</summary>
    public bool Available => Kind != SurfaceBackdropKind.None;

    /// <summary>显式不可用（无背景纹理/无捕获许可）。</summary>
    public static readonly SurfaceBackdropFrame Unavailable = new(
        SurfaceBackdropKind.None,
        new SurfaceLightCoverage(0f, 0f, 1f, 1f),
        96,
        0f,
        0f,
        0L,
        null,
        "backdrop-unavailable");

    /// <summary>该来源可达的玻璃上限。</summary>
    public GlassTier MaxTier => Kind switch
    {
        SurfaceBackdropKind.PageContent => GlassTier.T0,
        SurfaceBackdropKind.ParentSnapshot => GlassTier.T2,
        SurfaceBackdropKind.ScreenCapture => GlassTier.T3,
        SurfaceBackdropKind.EngineTexture => GlassTier.T3,
        SurfaceBackdropKind.EngineScene => GlassTier.T4,
        _ => GlassTier.Unsupported,
    };
}
