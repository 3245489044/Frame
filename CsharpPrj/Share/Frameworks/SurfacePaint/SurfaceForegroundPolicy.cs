#nullable enable

using System.Collections.Generic;
using MathematicalAlgorithms.Color;

namespace Share.SurfacePaint;

/// <summary>
/// 前景色裁决（载体无关单源）：WCAG 2.x 相对亮度 + 对比度比。
/// <para>属性通道与自绘外壳共用本类，禁止在载体侧再写一份亮度公式。</para>
/// <para>判据：优先返回满足 <see cref="MinimumContrastRatio"/> 的候选；否则返回对比度更高的极色。</para>
/// <para>算术单源 = <see cref="ContrastMath"/>（Plate.Mathematics）；本类只固定墨水色与阈值。</para>
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
        => ContrastMath.RelativeLuminance(argb);

    /// <summary>WCAG 对比度比（1..21）；参数顺序无关。</summary>
    /// <param name="a">颜色 A。</param>
    /// <param name="b">颜色 B。</param>
    public static double ContrastRatio(int a, int b)
        => ContrastMath.ContrastRatio(a, b);

    /// <summary>给定底色，返回满足对比度的前景色（黑/白极色中比值更高者）。</summary>
    /// <param name="backgroundArgb">面底色（不透明）。</param>
    public static int ForegroundFor(int backgroundArgb)
        => ContrastMath.ForegroundFor(backgroundArgb, LightInkArgb, DarkInkArgb);

    /// <summary>从候选前景色中选第一个达标的；都不达标 ⇒ 返回对比度最高者。</summary>
    /// <param name="backgroundArgb">面底色（不透明）。</param>
    /// <param name="candidates">候选前景色（按优先级）。</param>
    public static int Choose(int backgroundArgb, IReadOnlyList<int> candidates)
        => ContrastMath.Choose(
            backgroundArgb, candidates, LightInkArgb, DarkInkArgb, MinimumContrastRatio);
}
