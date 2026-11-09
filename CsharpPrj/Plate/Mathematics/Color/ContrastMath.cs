#nullable enable

using System;
using System.Collections.Generic;

namespace MathematicalAlgorithms.Color;

/// <summary>
/// WCAG 2.x 对比度数学（**纯数值**）：相对亮度 + 对比度比 + 前景极色裁决。
/// <para>线性化阈值 0.03928 与 CIE Lab 的 0.04045 不同定义，不与 <see cref="ColorScience"/> 混用。</para>
/// </summary>
public static class ContrastMath
{
    /// <summary>WCAG 相对亮度（0..1；忽略 alpha）。</summary>
    public static double RelativeLuminance(int argb)
    {
        var r = Linearize((argb >> 16) & 0xFF);
        var g = Linearize((argb >> 8) & 0xFF);
        var b = Linearize(argb & 0xFF);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    /// <summary>对比度比（1..21；参数顺序无关）。</summary>
    public static double ContrastRatio(int a, int b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var lighter = Math.Max(la, lb);
        var darker = Math.Min(la, lb);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>给定底色，在两个极色墨中取对比度更高者。</summary>
    public static int ForegroundFor(int backgroundArgb, int lightInkArgb, int darkInkArgb)
        => ContrastRatio(darkInkArgb, backgroundArgb) >= ContrastRatio(lightInkArgb, backgroundArgb)
            ? darkInkArgb
            : lightInkArgb;

    /// <summary>从候选前景色中选第一个达标的；都不达标 ⇒ 返回对比度最高者。</summary>
    public static int Choose(
        int backgroundArgb, IReadOnlyList<int> candidates,
        int lightInkArgb, int darkInkArgb, double minimumRatio)
    {
        if (candidates is null || candidates.Count == 0)
        {
            return ForegroundFor(backgroundArgb, lightInkArgb, darkInkArgb);
        }

        var best = candidates[0];
        var bestRatio = ContrastRatio(best, backgroundArgb);
        foreach (var candidate in candidates)
        {
            var ratio = ContrastRatio(candidate, backgroundArgb);
            if (ratio >= minimumRatio) return candidate;
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
