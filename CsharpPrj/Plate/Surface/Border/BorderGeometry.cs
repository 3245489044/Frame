#nullable enable

using System;

namespace UiBackend;

/// <summary>
/// 边框环几何（外沿 / 内沿，设备像素浮点）：描边外沿贴 bounds，内沿按四边独立内缩。
/// </summary>
/// <param name="OuterLeft">外沿左（设备像素）。</param>
/// <param name="OuterTop">外沿上。</param>
/// <param name="OuterRight">外沿右。</param>
/// <param name="OuterBottom">外沿下。</param>
/// <param name="InnerLeft">内沿左。</param>
/// <param name="InnerTop">内沿上。</param>
/// <param name="InnerRight">内沿右。</param>
/// <param name="InnerBottom">内沿下。</param>
/// <param name="InnerRadii">内沿四角半径（设备像素）。</param>
public readonly record struct BorderRing(
    double OuterLeft, double OuterTop, double OuterRight, double OuterBottom,
    double InnerLeft, double InnerTop, double InnerRight, double InnerBottom,
    CornerRadii InnerRadii)
{
    /// <summary>内沿是否仍有正面积（宽度未吃满整块）。</summary>
    public bool HasInner => InnerRight > InnerLeft && InnerBottom > InnerTop;

    /// <summary>内沿宽度。</summary>
    public double InnerWidth => InnerRight - InnerLeft;

    /// <summary>内沿高度。</summary>
    public double InnerHeight => InnerBottom - InnerTop;
}

/// <summary>边框几何（BCL-only 纯函数，无 GDI/CSS 方言）。</summary>
public static class BorderGeometry
{
    /// <summary>外沿 + 内沿（四边独立内缩）与内圆角。</summary>
    /// <param name="left">外沿左。</param>
    /// <param name="top">外沿上。</param>
    /// <param name="right">外沿右。</param>
    /// <param name="bottom">外沿下。</param>
    /// <param name="widths">四边内缩（设备像素）。</param>
    /// <param name="radii">四角外沿半径（设备像素）。</param>
    public static BorderRing Ring(
        double left, double top, double right, double bottom, BorderEdgeSet widths, CornerRadii radii)
    {
        var innerLeft = left + Math.Max(0, widths.Left);
        var innerTop = top + Math.Max(0, widths.Top);
        var innerRight = right - Math.Max(0, widths.Right);
        var innerBottom = bottom - Math.Max(0, widths.Bottom);
        return new BorderRing(
            left, top, right, bottom,
            innerLeft, innerTop, innerRight, innerBottom,
            InsetRadii(radii, widths));
    }

    /// <summary>内圆角 = 外圆角减去相邻两边内缩均值（同心，四边不等宽时取折中）。</summary>
    /// <param name="radii">外沿四角半径。</param>
    /// <param name="widths">四边内缩。</param>
    public static CornerRadii InsetRadii(CornerRadii radii, BorderEdgeSet widths)
    {
        var topLeft = Math.Max(0, radii.TopLeft - (widths.Top + widths.Left) / 2f);
        var topRight = Math.Max(0, radii.TopRight - (widths.Top + widths.Right) / 2f);
        var bottomRight = Math.Max(0, radii.BottomRight - (widths.Bottom + widths.Right) / 2f);
        var bottomLeft = Math.Max(0, radii.BottomLeft - (widths.Bottom + widths.Left) / 2f);
        return new CornerRadii(topLeft, topRight, bottomRight, bottomLeft);
    }

    /// <summary>DIP 四角半径 → 设备像素（浮点，不取整）。</summary>
    /// <param name="radii">DIP 四角半径。</param>
    /// <param name="dpi">设备 DPI。</param>
    public static CornerRadii ToPixels(CornerRadii radii, float dpi) => new(
        BorderUnits.DipsToPixels(radii.TopLeft, dpi),
        BorderUnits.DipsToPixels(radii.TopRight, dpi),
        BorderUnits.DipsToPixels(radii.BottomRight, dpi),
        BorderUnits.DipsToPixels(radii.BottomLeft, dpi));

    /// <summary>把四角半径夹到盒内（≤ 短边一半，防弧参数非法）。</summary>
    /// <param name="radii">四角半径。</param>
    /// <param name="width">盒宽。</param>
    /// <param name="height">盒高。</param>
    public static CornerRadii Clamp(CornerRadii radii, double width, double height)
    {
        var limit = (float)(Math.Max(0, Math.Min(width, height)) / 2.0);
        return new CornerRadii(
            Limit(radii.TopLeft, limit), Limit(radii.TopRight, limit),
            Limit(radii.BottomRight, limit), Limit(radii.BottomLeft, limit));
    }

    private static float Limit(float value, float limit)
        => value <= 0 ? 0 : value >= limit ? limit : value;
}
