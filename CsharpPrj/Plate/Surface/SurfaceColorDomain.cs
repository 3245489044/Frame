#nullable enable

using System;

namespace UiBackend;

/// <summary>
/// 颜色值域分类（**唯一实现**）：可判定「这个色值需要 alpha 通道」。
/// <para>用途：能力矩阵的 domain 判据（属性通道只接受 <see cref="Opaque"/>）；载体投影器禁各自猜 alpha。</para>
/// <para>无法判定的值（token 名/非法串）⇒ <see cref="Unknown"/>（不参与 domain 判定，禁静默当成不透明）。</para>
/// </summary>
public static class SurfaceColorDomain
{
    /// <summary>不透明色（可安全写入只支持不透明的通道）。</summary>
    public const string Opaque = "color";

    /// <summary>含 alpha 的色值（只可写入支持 alpha 的通道）。</summary>
    public const string Alpha = "color-alpha";

    /// <summary>不可判定（非颜色语法；调用方按「不检查」处理）。</summary>
    public const string Unknown = "";

    /// <summary>分类色值文本（hex / <c>rgb()</c>/<c>rgba()</c> / <c>r,g,b[,a]</c> / <c>transparent</c> / <c>none</c> / 多色 <c>c1,c2</c>）。</summary>
    /// <param name="value">色值文本（可含首尾空白）。</param>
    public static string Classify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Unknown;
        var text = value.Trim();

        if (SurfaceColor.TryParseColor(text, out var argb)) return OpaqueOrAlpha(argb);

        // 多色（gradient 的 value = "c1,c2,..."）：逐段分类；任一不可判 ⇒ Unknown，任一含 alpha ⇒ Alpha。
        if (!text.Contains(',')) return Unknown;

        var anyAlpha = false;
        var segments = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2) return Unknown;

        foreach (var segment in segments)
        {
            if (!SurfaceColor.TryParseColor(segment, out var segmentArgb)) return Unknown;
            if (((segmentArgb >> 24) & 0xFF) != 255) anyAlpha = true;
        }

        return anyAlpha ? Alpha : Opaque;
    }

    private static string OpaqueOrAlpha(int argb)
        => ((argb >> 24) & 0xFF) == 255 ? Opaque : Alpha;
}

/// <summary>
/// 不透明合成（**唯一实现**）：把带 alpha 的前景叠到不透明背景上，得到属性通道可承载的 ARGB。
/// <para>BCL-only（打包 ARGB int，不引用 System.Drawing）；结果 alpha 恒为 255。</para>
/// </summary>
public static class SurfaceColorComposition
{
    /// <summary>前景 over 背景 → 不透明 ARGB。</summary>
    /// <param name="foregroundArgb">前景（0xAARRGGBB）。</param>
    /// <param name="backgroundArgb">背景（应不透明；alpha 会按同公式参与混合）。</param>
    public static int Over(int foregroundArgb, int backgroundArgb)
    {
        var alpha = (foregroundArgb >> 24) & 0xFF;
        if (alpha == 255) return foregroundArgb;
        if (alpha == 0) return Opaque(backgroundArgb);

        int Mix(int foreground, int background)
            => (foreground * alpha + background * (255 - alpha) + 127) / 255;

        var r = Mix((foregroundArgb >> 16) & 0xFF, (backgroundArgb >> 16) & 0xFF);
        var g = Mix((foregroundArgb >> 8) & 0xFF, (backgroundArgb >> 8) & 0xFF);
        var b = Mix(foregroundArgb & 0xFF, backgroundArgb & 0xFF);
        return unchecked((255 << 24) | (r << 16) | (g << 8) | b);
    }

    private static int Opaque(int argb) => unchecked((255 << 24) | (argb & 0x00FFFFFF));
}
