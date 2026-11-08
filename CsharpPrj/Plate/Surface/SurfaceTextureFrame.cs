#nullable enable

namespace UiBackend;

/// <summary>玻璃背后内容源的统一数据形态（跨宿主/泵/引擎/诊断，只有一个类型）。</summary>
public enum SurfaceTextureKind
{
    /// <summary>无来源（显式不可用）。</summary>
    None = 0,

    /// <summary>页内内容（HTML backdrop-filter 定位区）。</summary>
    PageContent = 1,

    /// <summary>父级表面快照（WinForms 子树截图）。</summary>
    ParentSnapshot = 2,

    /// <summary>屏幕捕获（逐像素桌面透明）。</summary>
    ScreenCapture = 3,

    /// <summary>引擎背景纹理（离屏 RT）。</summary>
    EngineTexture = 4,
}

/// <summary>
/// 背景纹理帧（**跨节点唯一形态**）：宿主 Bitmap → 共享内存 → 引擎 D3D11Texture 均用本类型描述。
/// <para><see cref="Content"/> 是载体私有载荷（Bitmap 或 shm 名），跨层不解释；<see cref="Transport"/> 说明载荷形态。</para>
/// <para>缺省 = <see cref="Unavailable"/>；<see cref="Available"/> 为 false 时调用方必须显式降级，禁以填充冒充。</para>
/// </summary>
/// <param name="Kind">来源种类。</param>
/// <param name="Coverage">覆盖的 UI 空间（屏幕 DIP）。</param>
/// <param name="Dpi">来源 DPI（坐标换算唯一依据）。</param>
/// <param name="OriginX">来源原点 X（屏幕 DIP）。</param>
/// <param name="OriginY">来源原点 Y（屏幕 DIP）。</param>
/// <param name="Version">内容版本（缓存键；内容变化必须递增/换值）。</param>
/// <param name="Content">载荷（Bitmap / shm 名 / 引擎纹理；跨层不解释）。</param>
/// <param name="Transport">载荷形态：<c>bitmap</c> | <c>shm</c> | <c>texture</c>。</param>
/// <param name="Reason">不可用原因码。</param>
/// <param name="Detail">结构事实明细（诊断用）。</param>
public sealed record SurfaceTextureFrame(
    SurfaceTextureKind Kind,
    SurfaceLightCoverage Coverage,
    int Dpi,
    float OriginX,
    float OriginY,
    long Version,
    object? Content = null,
    string Transport = "",
    string Reason = "",
    string Detail = "")
{
    /// <summary>是否有真实内容源。</summary>
    public bool Available => Kind != SurfaceTextureKind.None;

    /// <summary>显式不可用（无背景纹理/无捕获许可）。</summary>
    public static readonly SurfaceTextureFrame Unavailable = new(
        SurfaceTextureKind.None,
        new SurfaceLightCoverage(0f, 0f, 1f, 1f),
        96,
        0f,
        0f,
        0L,
        null,
        string.Empty,
        "backdrop-unavailable");

    /// <summary>该来源可达的玻璃上限。</summary>
    public GlassTier MaxTier => Kind switch
    {
        SurfaceTextureKind.PageContent => GlassTier.T0,
        SurfaceTextureKind.ParentSnapshot => GlassTier.T2,
        SurfaceTextureKind.ScreenCapture => GlassTier.T3,
        SurfaceTextureKind.EngineTexture => GlassTier.T3,
        _ => GlassTier.Unsupported,
    };
}

/// <summary>
/// 过渡适配：旧 <see cref="SurfaceBackdropFrame"/> 与 <see cref="SurfaceTextureFrame"/> 互转（保留一个批次）。
/// <para>新代码一律用 <see cref="SurfaceTextureFrame"/>；旧类型只作为别名存在。</para>
/// </summary>
public static class SurfaceTextureFrameAdapter
{
    /// <summary>旧 → 新。</summary>
    public static SurfaceTextureFrame ToTextureFrame(this SurfaceBackdropFrame frame)
    {
        if (frame is null) return SurfaceTextureFrame.Unavailable;
        return new SurfaceTextureFrame(
            ToTextureKind(frame.Kind),
            frame.Coverage,
            frame.Dpi,
            frame.OriginX,
            frame.OriginY,
            frame.Version,
            frame.Handle,
            frame.Handle is string ? "shm" : frame.Handle is null ? string.Empty : "bitmap",
            frame.Reason,
            frame.Detail);
    }

    /// <summary>新 → 旧（选层/兼容读取面）。</summary>
    public static SurfaceBackdropFrame FromTextureFrame(this SurfaceTextureFrame frame)
    {
        if (frame is null) return SurfaceBackdropFrame.Unavailable;
        return new SurfaceBackdropFrame(
            ToBackdropKind(frame.Kind),
            frame.Coverage,
            frame.Dpi,
            frame.OriginX,
            frame.OriginY,
            frame.Version,
            frame.Content,
            frame.Reason,
            frame.Detail);
    }

    private static SurfaceTextureKind ToTextureKind(SurfaceBackdropKind kind) => kind switch
    {
        SurfaceBackdropKind.PageContent => SurfaceTextureKind.PageContent,
        SurfaceBackdropKind.ParentSnapshot => SurfaceTextureKind.ParentSnapshot,
        SurfaceBackdropKind.ScreenCapture => SurfaceTextureKind.ScreenCapture,
        SurfaceBackdropKind.EngineTexture => SurfaceTextureKind.EngineTexture,
        SurfaceBackdropKind.EngineScene => SurfaceTextureKind.EngineTexture,
        _ => SurfaceTextureKind.None,
    };

    private static SurfaceBackdropKind ToBackdropKind(SurfaceTextureKind kind) => kind switch
    {
        SurfaceTextureKind.PageContent => SurfaceBackdropKind.PageContent,
        SurfaceTextureKind.ParentSnapshot => SurfaceBackdropKind.ParentSnapshot,
        SurfaceTextureKind.ScreenCapture => SurfaceBackdropKind.ScreenCapture,
        SurfaceTextureKind.EngineTexture => SurfaceBackdropKind.EngineTexture,
        _ => SurfaceBackdropKind.None,
    };
}
