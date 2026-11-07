#nullable enable

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 几何内核（K2，GDI+ 方言落点）：圆角路径 + DIP→设备像素的浮点换算，**唯一实现**。
/// <para>fill / gradient / shadow / innerShadow / backdrop / specular / stroke 全部共用本类，禁各自建路径；
/// 描边的四边内缩与内圆角仍由 <see cref="BorderGeometry"/>（BCL-only）给出，本类只负责落成 <see cref="GraphicsPath"/>。</para>
/// <para>半径按设备像素浮点处理，不取整：只在小数宽度/半径上保真，避免「(int)」截断造成的半像素丢墨。</para>
/// </summary>
internal static class SkinGeometry
{
    /// <summary>按盒边界 + DIP 半径生成圆角路径（radius≤0 或盒退化 ⇒ null，调用方走矩形快路径）。</summary>
    /// <param name="bounds">盒边界（设备像素）。</param>
    /// <param name="radiusDips">圆角半径（DIP；≤0 ⇒ 直角）。</param>
    /// <param name="dpi">设备 DPI。</param>
    internal static GraphicsPath? Shaped(Rectangle bounds, float radiusDips, float dpi)
    {
        if (radiusDips <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return null;
        var radius = BorderGeometry.Clamp(
            CornerRadii.Uniform(BorderUnits.DipsToPixels(radiusDips, dpi)),
            bounds.Width, bounds.Height);
        return Path(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height), radius);
    }

    /// <summary>浮点圆角矩形路径（四角独立；radius 全 0 ⇒ 矩形路径）。</summary>
    /// <param name="rect">目标矩形（设备像素）。</param>
    /// <param name="radii">四角半径（设备像素）。</param>
    internal static GraphicsPath Path(RectangleF rect, CornerRadii radii)
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
        var limit = (float)(Math.Min(w, h) / 2.0);
        var topLeft = Limit(radii.TopLeft, limit);
        var topRight = Limit(radii.TopRight, limit);
        var bottomRight = Limit(radii.BottomRight, limit);
        var bottomLeft = Limit(radii.BottomLeft, limit);

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

    private static float Limit(float value, float limit)
        => value <= 0 ? 0 : value >= limit ? limit : value;
}
