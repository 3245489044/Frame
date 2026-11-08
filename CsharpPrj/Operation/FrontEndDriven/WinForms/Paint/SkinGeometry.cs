#nullable enable

using System.Drawing;
using System.Drawing.Drawing2D;
using Share.SurfacePaint;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>几何门面（薄层）：实现单源 = <see cref="SurfaceGeometry"/>（Share.SurfacePaint）。</summary>
internal static class SkinGeometry
{
    internal static GraphicsPath? Shaped(Rectangle bounds, float radiusDips, float dpi)
        => SurfaceGeometry.Shaped(bounds, radiusDips, dpi);

    internal static GraphicsPath? Shaped(RectangleF bounds, float radiusDips, float dpi)
        => SurfaceGeometry.Shaped(bounds, radiusDips, dpi);

    internal static RectangleF HalfPixelAlign(Rectangle bounds) => SurfaceGeometry.HalfPixelAlign(bounds);

    internal static GraphicsPath Path(RectangleF rect, CornerRadii radii) => SurfaceGeometry.Path(rect, radii);
}