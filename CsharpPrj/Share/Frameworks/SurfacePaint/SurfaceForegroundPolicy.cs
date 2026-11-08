#nullable enable

using System;
using System.Collections.Generic;

namespace Share.SurfacePaint;

/// <summary>
/// 前景色裁决（载体无关单源）：WCAG 2.x 相对亮度 + 对比度比。
/// <para>属性通道与自绘外壳共用本类，禁止在载体侧再写一份亮度公式。</para>
/// <para>判据：优先返回满足 <see cref="MinimumContrastRatio"/> 的候选；否则返回对比度更高的极色。</para>
/// </summary>
public static class SurfaceForegroundPolicy
{
    /// <summary>WCAG AA 正文最小对比度。</summary>
    public const double MinimumContrastRatio = 4.5;

    /// <summary>浅色墨（深底上用）。</summary>
    public const int LightInkArgb = unchecked((int)0xFFE6EDF3);

    /// <summary>深色墨（浅底上用）。</summary>
    public const int DarkInkArgb = unchecked((int)0xFF1E2228);

    /// <summary>WCAG 相对亮度（0..1）。</summary>
    /// <param name="argb">颜色（ARGB，忽略 alpha）。</param>
    public static double RelativeLuminance(int argb)
    {
        var r = Linearize((argb >> 16) & 0xFF);
        var g = Linearize((argb >> 8) & 0xFF);
        var b = Linearize(argb & 0xFF);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    /// <summary>WCAG 对比度比（1..21）；参数顺序无关。</summary>
    /// <param name="a">颜色 A。</param>
    /// <param name="b">颜色 B。</param>
    public static double ContrastRatio(int a, int b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var lighter = Math.Max(la, lb);
        var darker = Math.Min(la, lb);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>给定底色，返回满足对比度的前景色（黑/白极色中比值更高者）。</summary>
    /// <param name="backgroundArgb">面底色（不透明）。</param>
    public static int ForegroundFor(int backgroundArgb)
    {
        var light = ContrastRatio(LightInkArgb, backgroundArgb);
        var dark = ContrastRatio(DarkInkArgb, backgroundArgb);
        return dark >= light ? DarkInkArgb : LightInkArgb;
    }

    /// <summary>从候选前景色中选第一个达标的；都不达标 ⇒ 返回对比度最高者。</summary>
    /// <param name="backgroundArgb">面底色（不透明）。</param>
    /// <param name="candidates">候选前景色（按优先级）。</param>
    public static int Choose(int backgroundArgb, IReadOnlyList<int> candidates)
    {
        if (candidates is null || candidates.Count == 0) return ForegroundFor(backgroundArgb);

        var best = candidates[0];
        var bestRatio = ContrastRatio(best, backgroundArgb);
        foreach (var candidate in candidates)
        {
            var ratio = ContrastRatio(candidate, backgroundArgb);
            if (ratio >= MinimumContrastRatio) return candidate;
            if (ratio > bestRatio)
            {
                best = candidate;
                bestRatio = ratio;
            }
        }

        return best;
    }

    private static double Linearize(int channel)
    {
        var c = channel / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
