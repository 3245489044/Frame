#nullable enable

using System.Drawing;
using Share.SurfacePaint;
using UiBackend;

namespace Share.SurfacePaint.Compat;

/// <summary>边框门面（薄层）：实现单源 = <see cref="SurfaceBorderPainter"/>。</summary>
public static class BorderPainter
{
    /// <summary>画一条描边（<see cref="BorderStroke"/> 已由 Plate 归一化）。</summary>
    public static void DrawStroke(Graphics graphics, Rectangle bounds, BorderStroke stroke, float dpi)
        => SurfaceBorderPainter.DrawStroke(graphics, bounds, stroke, dpi);
}
