#nullable enable

using System.Drawing;

namespace Share.SurfacePaint.Hosting;

/// <summary>
/// 宿主视口快照（**载体无关**）：控件客户区 + 所属宿主（窗体/页面）尺寸 + 屏幕原点。
/// <para>载体负责把 Control/WebView 的坐标换算填入本结构；框架层只读，不调用 PointToScreen/FindForm。</para>
/// <para>All dip values are device-independent pixels (96 DPI 基准)。</para>
/// </summary>
/// <param name="Width">控件客户区宽（px）。</param>
/// <param name="Height">控件客户区高（px）。</param>
/// <param name="Dpi">控件设备 DPI。</param>
/// <param name="HostWidth">宿主客户区宽（px；页面场尺寸）。</param>
/// <param name="HostHeight">宿主客户区高（px）。</param>
/// <param name="HostDpi">宿主设备 DPI。</param>
/// <param name="OffsetX">控件在宿主客户区内的 X 偏移（px）。</param>
/// <param name="OffsetY">控件在宿主客户区内的 Y 偏移（px）。</param>
/// <param name="HostOriginXDip">宿主客户区左上角的屏幕 X（DIP）。</param>
/// <param name="HostOriginYDip">宿主客户区左上角的屏幕 Y（DIP）。</param>
/// <param name="OriginXDip">控件客户区左上角的屏幕 X（DIP）。</param>
/// <param name="OriginYDip">控件客户区左上角的屏幕 Y（DIP）。</param>
public readonly record struct SurfaceViewport(
    int Width,
    int Height,
    int Dpi,
    int HostWidth,
    int HostHeight,
    int HostDpi,
    int OffsetX,
    int OffsetY,
    double HostOriginXDip,
    double HostOriginYDip,
    double OriginXDip,
    double OriginYDip)
{
    /// <summary>控件矩形（原点固定在 0,0）。</summary>
    public Rectangle Bounds => new(0, 0, Width, Height);

    /// <summary>控件 DPI 缩放（96 = 1.0）。</summary>
    public float Scale => Dpi > 0 ? Dpi / 96f : 1f;

    /// <summary>宿主 DPI 缩放。</summary>
    public float HostScale => HostDpi > 0 ? HostDpi / 96f : 1f;

    /// <summary>宿主客户区宽（DIP）：与控件 DIP 原点同口径，光探针/覆盖区一律取本值。</summary>
    public double HostWidthDip => HostDpi > 0 ? HostWidth * 96.0 / HostDpi : HostWidth;

    /// <summary>宿主客户区高（DIP）。</summary>
    public double HostHeightDip => HostDpi > 0 ? HostHeight * 96.0 / HostDpi : HostHeight;

    /// <summary>空视口（无尺寸 ⇒ 绘制应直接跳过）。</summary>
    public static readonly SurfaceViewport Empty = new(0, 0, 96, 0, 0, 96, 0, 0, 0, 0, 0, 0);

    /// <summary>是否可绘制。</summary>
    public bool IsValid => Width > 0 && Height > 0;

    /// <summary>以指定客户区尺寸重算（宿主尺寸/原点不变）。</summary>
    public SurfaceViewport WithSize(int width, int height)
        => this with { Width = width, Height = height };

    /// <summary>
    /// 卡片视口（**唯一换算入口**）：DIP 矩形 → 像素视口（宽高/偏移为像素，原点为屏幕 DIP）。
    /// <para>卡片的位图与引擎请求都按像素尺寸；调用方不得再各自乘/除 scale。</para>
    /// </summary>
    /// <param name="boundsDip">卡片在宿主客户区内的矩形（DIP）。</param>
    public SurfaceViewport Card(Rectangle boundsDip)
    {
        var scale = Scale;
        return this with
        {
            Width = Math.Max(1, (int)Math.Round(boundsDip.Width * scale)),
            Height = Math.Max(1, (int)Math.Round(boundsDip.Height * scale)),
            OffsetX = (int)Math.Round(boundsDip.X * scale),
            OffsetY = (int)Math.Round(boundsDip.Y * scale),
            OriginXDip = HostOriginXDip + boundsDip.X,
            OriginYDip = HostOriginYDip + boundsDip.Y,
        };
    }
}
