#nullable enable

using System;

namespace MathematicalAlgorithms.Surface;

/// <summary>
/// 光栅原语的标量数学（**纯数值**）：掩膜衰减、模糊降采样因子、丝带列几何与色相。
/// <para>像素读写在 <c>Share.SurfacePaint.Raster</c>（需要 Bitmap/Graphics）；本类只给标量公式。</para>
/// </summary>
public static class SurfaceRasterMath
{
    /// <summary>径向掩膜中心到边缘的归一化距离（中心 0、边界 0.999…）。</summary>
    public static double SpotMaskDistance(int x, int y, int side)
    {
        var center = (side - 1) / 2.0;
        var dx = (x - center) / center;
        var dy = (y - center) / center;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>径向掩膜 alpha（距离 ≥1 ⇒ 0；否则 255×(1-d)²）。</summary>
    public static int SpotMaskAlpha(double distance)
        => distance >= 1.0 ? 0 : (int)Math.Round(255 * (1 - distance) * (1 - distance));

    /// <summary>模糊降采样因子（radius≤0 不在本函数口径内；18 ⇒ 4，约 4×4 降采样）。</summary>
    public static int BlurDownsampleFactor(int radius)
        => Math.Max(2, radius / 4);

    /// <summary>降采样后的边长（至少 1px）。</summary>
    public static int BlurDownsampleSize(int length, int factor)
        => Math.Max(1, length / factor);

    /// <summary>丝带逐列法线半高（带宽 half 是垂直距离 ⇒ half/√(1+slope²)）。</summary>
    public static float RibbonHalfHeight(float bandWidth, float slope)
        => bandWidth * 0.5f / MathF.Sqrt(1f + slope * slope);

    /// <summary>丝带该列的色相（沿页宽线性推进）。</summary>
    public static float RibbonHue(float hue0, int x, int width, float span)
        => hue0 + Math.Clamp(x / Math.Max(1f, width), 0f, 1f) * span;

    /// <summary>丝带中心线（基准斜率 0.34 + 正弦振幅）。</summary>
    public static float RibbonCenterline(float centerX, float centerY, float amplitude, float phase, float x)
        => centerY + (x - centerX) * 0.34f + amplitude * MathF.Sin(phase);

    /// <summary>丝带中心线斜率（0.34 + 振幅×角频率×cos）。</summary>
    public static float RibbonSlope(float amplitude, float radians, float phase)
        => 0.34f + amplitude * radians * MathF.Cos(phase);

    /// <summary>丝带波形角频率（2π/波长）。</summary>
    public static float RibbonRadians(float wavelength)
        => (float)(2 * Math.PI / wavelength);

    /// <summary>丝带该列的波形相位（几何相位只影响形状，不影响色相）。</summary>
    public static float RibbonPhase(float radians, float x, float centerX, float shapePhase)
        => radians * (x - centerX) + shapePhase * 2f * (float)Math.PI;
}
