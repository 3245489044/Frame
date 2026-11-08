#nullable enable

using System.Drawing;
using System.Drawing.Drawing2D;
using Share.SurfacePaint;
using UiBackend;

namespace Share.SurfacePaint.Compat;

/// <summary>几何门面（薄层）：实现单源 = <see cref="SurfaceGeometry"/>。</summary>
public static class SkinGeometry
{
    /// <summary>圆角路径（设备像素）。</summary>
    public static GraphicsPath? Shaped(Rectangle bounds, float radiusDips, float dpi)
        => SurfaceGeometry.Shaped(bounds, radiusDips, dpi);

    /// <summary>圆角路径（浮点边界）。</summary>
    public static GraphicsPath? Shaped(RectangleF bounds, float radiusDips, float dpi)
        => SurfaceGeometry.Shaped(bounds, radiusDips, dpi);

    /// <summary>半像素对齐。</summary>
    public static RectangleF HalfPixelAlign(Rectangle bounds) => SurfaceGeometry.HalfPixelAlign(bounds);

    /// <summary>按角半径构造路径。</summary>
    public static GraphicsPath Path(RectangleF rect, CornerRadii radii) => SurfaceGeometry.Path(rect, radii);
}
