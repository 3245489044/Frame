#nullable enable

using System;

namespace MathematicalAlgorithms.Lighting;

/// <summary>
/// 载体表面着色数学（**唯一实现**）：HSV 基色、暖度/底色混合、sheen/边缘权重、色相剖面。
/// <para>Engine 的 HLSL 着色常量由本类嵌入（<c>SurfaceShader.PixelBody</c>），两侧不再各写字面量。</para>
/// <para>纯标量/BCL：无 System.Drawing，无载体类型。</para>
/// </summary>
public static class SurfaceShadingMath
{
    /// <summary>镜面指数（与 HLSL <c>pow(..., exp)</c> 同源）。</summary>
    public const float SpecularExponent = 12f;

    /// <summary>镜面增益（与 HLSL <c>specular * gain</c> 同源）。</summary>
    public const float SpecularGain = 0.35f;

    /// <summary>暖度混合上限（与 HLSL <c>Warmth * mix</c> 同源）。</summary>
    public const float WarmthMix = 0.35f;

    /// <summary>环境色混合权重（GDI+ 路径的 tint 偏置）。</summary>
    public const float TintMix = 0.15f;

    /// <summary>RGB → HSV 色相（0..360；两载体环基色的同一取值口径）。</summary>
    public static float HueOf(int argb)
    {
        var (r, g, b) = RgbOf(argb);
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        if (delta <= 0) return 0f;

        float hue;
        if (max == r) hue = 60f * (((g - b) / (float)delta) % 6f);
        else if (max == g) hue = 60f * (((b - r) / (float)delta) + 2f);
        else hue = 60f * (((r - g) / (float)delta) + 4f);
        return hue < 0f ? hue + 360f : hue;
    }

    /// <summary>RGB → HSV 饱和度（0..1；用于区分彩色令牌与深档近白令牌）。</summary>
    public static float SaturationOf(int argb)
    {
        var (r, g, b) = RgbOf(argb);
        var max = Math.Max(r, Math.Max(g, b));
        if (max <= 0) return 0f;
        var min = Math.Min(r, Math.Min(g, b));
        return (max - min) / (float)max;
    }

    /// <summary>按时点暖度把颜色染向暖白（只调颜色，不改 alpha）。</summary>
    public static int Warm(int argb, float warmth)
    {
        if (warmth <= 0f) return argb;
        var (r, g, b) = RgbOf(argb);
        var t = Math.Clamp(warmth, 0f, 1f) * WarmthMix;
        return Pack(
            (argb >> 24) & 0xFF,
            (int)(r + (255 - r) * t),
            (int)(g + (224 - g) * t),
            (int)(b + (170 - b) * t));
    }

    /// <summary>高光色：暖度 + 环境色（无光场时只做暖度，保持声明值路径）。</summary>
    public static int LitColor(int argb, float warmth, int tintArgb, bool hasLight)
    {
        var lit = Warm(argb, warmth);
        if (!hasLight) return lit;

        var (lr, lg, lb) = RgbOf(lit);
        var (tr, tg, tb) = RgbOf(tintArgb);
        return Pack(
            (lit >> 24) & 0xFF,
            (int)Math.Round(lr * (1 - TintMix) + tr * TintMix),
            (int)Math.Round(lg * (1 - TintMix) + tg * TintMix),
            (int)Math.Round(lb * (1 - TintMix) + tb * TintMix));
    }

    /// <summary>
    /// 高光峰值：矩阵镜面权重决定强弱，声明 <paramref name="declaredStrength"/> 只作上限。
    /// <para>无光场（specular=0）⇒ 仍按 strength 保留静态路径；调用方以 shade/specular 传入。</para>
    /// </summary>
    public static int SheenPeak(float baseAlpha, float declaredStrength, float specular, float shade)
    {
        var gain = 0.25f + 0.75f * Math.Clamp(specular, 0f, 1f);
        var cap = Math.Clamp(declaredStrength, 0f, 1f);
        var lit = Math.Clamp(shade, 0f, 1f);
        return Math.Clamp((int)Math.Round(baseAlpha * gain * cap * lit), 0, 200);
    }

    /// <summary>单边高光 alpha（权重已由端口按最亮边归一到 1）。</summary>
    public static int EdgeAlpha(int peak, float weight)
        => Math.Clamp((int)Math.Round(peak * Math.Clamp(weight, 0f, 1f)), 0, 200);

    /// <summary>色相剖面停靠点的 alpha（peak × profile，0..255）。</summary>
    public static int SpectralStop(int peak, float profile)
        => Math.Clamp((int)Math.Round(peak * profile), 0, 255);

    private static (int R, int G, int B) RgbOf(int argb)
        => ((argb >> 16) & 0xFF, (argb >> 8) & 0xFF, argb & 0xFF);

    private static int Pack(int alpha, int r, int g, int b)
        => unchecked(
            (Math.Clamp(alpha, 0, 255) << 24)
            | (Math.Clamp(r, 0, 255) << 16)
            | (Math.Clamp(g, 0, 255) << 8)
            | Math.Clamp(b, 0, 255));
}
