#nullable enable

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using UiBackend;

namespace Share.SurfacePaint;

/// <summary>
/// 边框绘制（GDI+ 方言落点）：**环填充**而不是 pen 中心线描边。
/// <para>外沿贴 bounds（与 CSS border-box 同一锚点）；内沿按四边独立内缩；宽度/圆角按 DPI 浮点换算，不取整。</para>
/// <para>1 像素在 96dpi 时四边各覆盖一整列/行；1.25/1.5 像素 由抗锯齿保留分数，不会被拆成两列各 50%。</para>
/// </summary>
public static class SurfaceBorderPainter
{
    /// <summary>画一条描边（<see cref="BorderStroke"/> 已由 Plate 归一化）。</summary>
    /// <param name="graphics">目标画布。</param>
    /// <param name="bounds">盒边界（描边外沿）。</param>
    /// <param name="stroke">描边模型（DIP）。</param>
    /// <param name="dpi">设备 DPI。</param>
    public static void DrawStroke(Graphics graphics, Rectangle bounds, BorderStroke stroke, float dpi)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (stroke is null || !stroke.HasVisibleWidth || bounds.Width <= 0 || bounds.Height <= 0) return;

        var widths = new BorderEdgeSet(
            BorderUnits.DipsToPixels(stroke.Widths.Left, dpi),
            BorderUnits.DipsToPixels(stroke.Widths.Top, dpi),
            BorderUnits.DipsToPixels(stroke.Widths.Right, dpi),
            BorderUnits.DipsToPixels(stroke.Widths.Bottom, dpi));
        if (widths.IsZero) return;

        var radii = BorderGeometry.Clamp(
            BorderGeometry.ToPixels(stroke.Radii, dpi), bounds.Width, bounds.Height);
        // GDI+ 抗锯齿按像素中心采样：整数边界会各吃半像素，且位图右/下边把外沿半像素裁掉 ⇒ 左右不对称。
        // 整体平移 -0.5 像素，让外沿落在像素中心之间：四边各覆盖整列/行，宽度*设备倍率精确兑现。
        var outer = SurfaceGeometry.HalfPixelAlign(bounds);
        var ring = BorderGeometry.Ring(outer.Left, outer.Top, outer.Right, outer.Bottom, widths, radii);

        using var outerPath = SurfaceGeometry.Path(outer, radii);
        using var ringPath = new GraphicsPath(FillMode.Alternate);
        ringPath.AddPath(outerPath, false);
        if (ring.HasInner)
        {
            var innerRect = new RectangleF(
                (float)ring.InnerLeft, (float)ring.InnerTop,
                (float)ring.InnerWidth, (float)ring.InnerHeight);
            using var innerPath = SurfaceGeometry.Path(innerRect, ring.InnerRadii);
            ringPath.AddPath(innerPath, false);
        }

        if (stroke.Colors.IsUniform)
        {
            using var brush = new SolidBrush(Color.FromArgb(stroke.Colors.Left));
            graphics.FillPath(brush, ringPath);
            return;
        }

        // per-edge 颜色：clip 到环，再按边填带（角部按绘制序覆盖；模型不承诺 CSS 斜接细节）。
        var saved = graphics.Save();
        try
        {
            graphics.SetClip(ringPath, CombineMode.Intersect);
            FillBand(graphics, outer, widths.Left, stroke.Colors.Left, Side.Left);
            FillBand(graphics, outer, widths.Top, stroke.Colors.Top, Side.Top);
            FillBand(graphics, outer, widths.Right, stroke.Colors.Right, Side.Right);
            FillBand(graphics, outer, widths.Bottom, stroke.Colors.Bottom, Side.Bottom);
        }
        finally
        {
            graphics.Restore(saved);
        }
    }

    private enum Side
    {
        Left,
        Top,
        Right,
        Bottom,
    }

    private static void FillBand(Graphics graphics, RectangleF outer, float width, int argb, Side side)
    {
        if (width <= 0) return;
        var rect = side switch
        {
            Side.Left => new RectangleF(outer.X, outer.Y, width, outer.Height),
            Side.Top => new RectangleF(outer.X, outer.Y, outer.Width, width),
            Side.Right => new RectangleF(outer.Right - width, outer.Y, width, outer.Height),
            _ => new RectangleF(outer.X, outer.Bottom - width, outer.Width, width),
        };
        using var brush = new SolidBrush(Color.FromArgb(argb));
        graphics.FillRectangle(brush, rect);
    }

}
