#nullable enable

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using UiBackend;

namespace Share.SurfacePaint;

/// <summary>
/// 几何内核（K2，GDI+ 方言落点）：圆角路径 + DIP→设备像素的浮点换算，**唯一实现**。
/// <para>fill / gradient / shadow / innerShadow / backdrop / specular / stroke 全部共用本类，禁各自建路径；
/// 描边的四边内缩与内圆角仍由 <see cref="BorderGeometry"/>（BCL-only）给出，本类只负责落成 <see cref="GraphicsPath"/>。</para>
/// <para>半径按设备像素浮点处理，不取整：只在小数宽度/半径上保真，避免「(int)」截断造成的半像素丢墨。</para>
/// </summary>
public static class SurfaceGeometry
{
    /// <summary>按盒边界 + DIP 半径生成圆角路径（radius≤0 或盒退化 ⇒ null，调用方走矩形快路径）。</summary>
    /// <param name="bounds">盒边界（设备像素）。</param>
    /// <param name="radiusDips">圆角半径（DIP；≤0 ⇒ 直角）。</param>
    /// <param name="dpi">设备 DPI。</param>
    public static GraphicsPath? Shaped(Rectangle bounds, float radiusDips, float dpi)
        => Shaped(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height), radiusDips, dpi);

    /// <summary>同上（浮点盒）；阴影/光斑等按声明偏移与扩散落成的层走此重载，不做整数截断。</summary>
    /// <param name="bounds">盒边界（设备像素，允许小数）。</param>
    /// <param name="radiusDips">圆角半径（DIP；≤0 ⇒ 直角）。</param>
    /// <param name="dpi">设备 DPI。</param>
    public static GraphicsPath? Shaped(RectangleF bounds, float radiusDips, float dpi)
    {
        if (radiusDips <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return null;

        // P1·小元素半径上限：短边 < 120 DIP 的按钮/小卡半径夹 12 DIP，
        // 防大令牌半径（面板 34 DIP）把小控件画成胶囊/圆斑；
        // 深档半径令牌均 ≤ 10 DIP，故深档像素零差异（DarkTier 硬约束）。
        const float SmallElementShortSideDips = 120f;
        const float SmallElementMaxRadiusDips = 12f;
        var dpiSafe = dpi > 0 ? dpi : BorderUnits.BaseDpi;
        var shortSideDips = Math.Min(bounds.Width, bounds.Height) * BorderUnits.BaseDpi / dpiSafe;
        var effectiveRadiusDips = shortSideDips < SmallElementShortSideDips
            ? Math.Min(radiusDips, SmallElementMaxRadiusDips)
            : radiusDips;

        var radius = BorderGeometry.Clamp(
            CornerRadii.Uniform(BorderUnits.DipsToPixels(effectiveRadiusDips, dpi)),
            bounds.Width, bounds.Height);
        return Path(bounds, radius);
    }

    /// <summary>
    /// 描边落点对齐（半像素）：GDI+ 抗锯齿按像素中心采样，整数边界会让外沿各吃半像素。
    /// 整体平移 -0.5 像素，使外沿落在像素中心之间；四边宽度一致、位图右/下边不被裁掉半像素。
    /// </summary>
    /// <param name="bounds">逻辑盒边界（设备像素）。</param>
    public static RectangleF HalfPixelAlign(Rectangle bounds)
        => new(bounds.X - 0.5f, bounds.Y - 0.5f, bounds.Width, bounds.Height);

    /// <summary>浮点圆角矩形路径（四角独立；radius 全 0 ⇒ 矩形路径）。</summary>
    /// <param name="rect">目标矩形（设备像素）。</param>
    /// <param name="radii">四角半径（设备像素）。</param>
    public static GraphicsPath Path(RectangleF rect, CornerRadii radii)
    {
        var path = new GraphicsPath();
        if (rect.Width <= 0 || rect.Height <= 0) return path;
        if (radii.IsZero)
        {
            path.AddRectangle(rect);
            return path;
        }

        var x = rect.X;
        var y = rect.Y;
        var w = rect.Width;
        var h = rect.Height;
        // 夹取唯一实现：BorderGeometry.Clamp（BCL-only）；本类不再自带一份 Limit。
        var clamped = BorderGeometry.Clamp(radii, w, h);
        var topLeft = clamped.TopLeft;
        var topRight = clamped.TopRight;
        var bottomRight = clamped.BottomRight;
        var bottomLeft = clamped.BottomLeft;

        path.StartFigure();
        path.AddLine(x + topLeft, y, x + w - topRight, y);
        if (topRight > 0) path.AddArc(x + w - topRight * 2, y, topRight * 2, topRight * 2, 270, 90);
        else path.AddLine(x + w, y, x + w, y);

        path.AddLine(x + w, y + topRight, x + w, y + h - bottomRight);
        if (bottomRight > 0) path.AddArc(
            x + w - bottomRight * 2, y + h - bottomRight * 2, bottomRight * 2, bottomRight * 2, 0, 90);
        else path.AddLine(x + w, y + h, x + w, y + h);

        path.AddLine(x + w - bottomRight, y + h, x + bottomLeft, y + h);
        if (bottomLeft > 0) path.AddArc(
            x, y + h - bottomLeft * 2, bottomLeft * 2, bottomLeft * 2, 90, 90);
        else path.AddLine(x, y + h, x, y + h);

        path.AddLine(x, y + h - bottomLeft, x, y + topLeft);
        if (topLeft > 0) path.AddArc(x, y, topLeft * 2, topLeft * 2, 180, 90);
        else path.AddLine(x, y, x, y);

        path.CloseFigure();
        return path;
    }
}
