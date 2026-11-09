#nullable enable

using System;

namespace MathematicalAlgorithms.Surface;

/// <summary>载体在屏幕上的物理矩形（DIP）：由宿主客户区原点与尺寸换算而来。</summary>
public readonly record struct SurfaceCarrierLoc(
    float ScreenXDip, float ScreenYDip, float WidthDip, float HeightDip, int Dpi);

/// <summary>宿主视口定位量（DIP/像素混排，字段口径与载体视口快照一致）。</summary>
public readonly record struct SurfaceViewportLoc(
    int Width, int Height, int Dpi,
    int HostWidth, int HostHeight, int HostDpi,
    int OffsetX, int OffsetY,
    double HostOriginXDip, double HostOriginYDip,
    double OriginXDip, double OriginYDip);

/// <summary>屏幕覆盖区（DIP）。</summary>
public readonly record struct SurfaceCoverageLoc(float X, float Y, float Width, float Height);

/// <summary>取整后的屏幕矩形（像素）。</summary>
public readonly record struct SurfaceScreenRectLoc(int X, int Y, int Width, int Height);

/// <summary>
/// 位置/坐标换算（**纯数值、零载体类型**）：把「控件客户区 + 屏幕原点 + DPI 比例」
/// 换算成视口/载体帧/覆盖区。调用方只负责取 Control 实测值与 <c>scale</c>（96/dpi）。
/// <para>DPI 归一与 <c>scale</c> 求值仍走 <c>BorderUnits</c> 单源；本类只承载坐标算术。</para>
/// </summary>
public static class SurfaceLocMath
{
    /// <summary>控件客户区 → 屏幕 DIP 载体帧。</summary>
    /// <param name="originX">客户区左上角屏幕 X（px）。</param>
    /// <param name="originY">客户区左上角屏幕 Y（px）。</param>
    /// <param name="clientWidth">客户区宽（px）。</param>
    /// <param name="clientHeight">客户区高（px）。</param>
    /// <param name="scale">DIP 比例（96/dpi）。</param>
    /// <param name="dpi">设备 DPI（已归一）。</param>
    public static SurfaceCarrierLoc CarrierOf(
        int originX, int originY, int clientWidth, int clientHeight, float scale, int dpi)
        => new(originX * scale, originY * scale, clientWidth * scale, clientHeight * scale, dpi);

    /// <summary>宿主客户区（原点已是 DIP、尺寸为 px）→ 载体帧（尺寸按 scale 换算为 DIP）。</summary>
    public static SurfaceCarrierLoc CarrierFromHost(
        double screenXDip, double screenYDip, int widthPx, int heightPx, float scale, int dpi)
        => new((float)screenXDip, (float)screenYDip, widthPx * scale, heightPx * scale, dpi);

    /// <summary>控件/窗体实测值 → 视口定位量（偏移为像素差，原点换算为 DIP）。</summary>
    /// <param name="width">控件客户区宽（px）。</param>
    /// <param name="height">控件客户区高（px）。</param>
    /// <param name="dpi">控件 DPI（已归一）。</param>
    /// <param name="hostWidth">宿主客户区宽（px）。</param>
    /// <param name="hostHeight">宿主客户区高（px）。</param>
    /// <param name="hostDpi">宿主 DPI（已归一）。</param>
    /// <param name="offsetX">控件在宿主客户区内的 X 偏移（px）。</param>
    /// <param name="offsetY">控件在宿主客户区内的 Y 偏移（px）。</param>
    /// <param name="hostOriginX">宿主客户区左上角屏幕 X（px）。</param>
    /// <param name="hostOriginY">宿主客户区左上角屏幕 Y（px）。</param>
    /// <param name="originX">控件客户区左上角屏幕 X（px）。</param>
    /// <param name="originY">控件客户区左上角屏幕 Y（px）。</param>
    /// <param name="controlScale">控件 DIP 比例（96/dpi）。</param>
    /// <param name="hostScale">宿主 DIP 比例（96/宿主 DPI）。</param>
    public static SurfaceViewportLoc ViewportOf(
        int width, int height, int dpi,
        int hostWidth, int hostHeight, int hostDpi,
        int offsetX, int offsetY,
        int hostOriginX, int hostOriginY, int originX, int originY,
        float controlScale, float hostScale)
        => new(
            width, height, dpi,
            hostWidth, hostHeight, hostDpi,
            offsetX, offsetY,
            hostOriginX * hostScale, hostOriginY * hostScale,
            originX * controlScale, originY * controlScale);

    /// <summary>虚拟屏像素矩形 → 覆盖区（DIP；宽高至少 1）。</summary>
    public static SurfaceCoverageLoc ScreenCoverageOf(int x, int y, int width, int height, float scale)
        => new(x * scale, y * scale, Math.Max(1f, width * scale), Math.Max(1f, height * scale));

    /// <summary>虚拟屏像素矩形 → 取整 DIP 矩形（宽高至少 1）。</summary>
    public static SurfaceScreenRectLoc RoundedScreenRectOf(int x, int y, int width, int height, float scale)
        => new(
            (int)Math.Round(x * scale),
            (int)Math.Round(y * scale),
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));

    /// <summary>宿主客户区（DIP）→ 光场覆盖区（宽高至少 1）。</summary>
    public static SurfaceCoverageLoc HostCoverageOf(
        double hostOriginXDip, double hostOriginYDip, double hostWidthDip, double hostHeightDip)
        => new(
            (float)hostOriginXDip,
            (float)hostOriginYDip,
            (float)Math.Max(1.0, hostWidthDip),
            (float)Math.Max(1.0, hostHeightDip));
}
