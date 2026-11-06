#nullable enable

using System;

namespace UiBackend;

/// <summary>
/// 表面色值编解码（**颜色格式转换的唯一实现**）。
/// <para>CSS 8 位 hex = <c>#RRGGBBAA</c>（alpha 在最后）；GDI+/Win32 打包值 = <c>0xAARRGGBB</c>（alpha 在最前）。
/// 两种排布差异只在本类转换，载体不再各自解释：CSS 侧原样投影，GDI+ 侧 <c>Color.FromArgb(argb)</c>。</para>
/// <para>只做「色值字符串 ↔ 打包 ARGB」转换，不引入新的颜色模型/材质抽象。</para>
/// </summary>
public static class SurfaceColor
{
    /// <summary>解析 <c>#RGB</c> / <c>#RGBA</c> / <c>#RRGGBB</c> / <c>#RRGGBBAA</c>（CSS 语义；8 位 = RRGGBBAA）。</summary>
    /// <param name="text">色值文本（可含首尾空白）。</param>
    /// <param name="argb">打包 ARGB（<c>0xAARRGGBB</c>，可直接传 <c>Color.FromArgb</c>）。</param>
    public static bool TryParseHex(string? text, out int argb)
    {
        argb = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var value = text.Trim();
        if (value.Length == 0 || value[0] != '#') return false;

        var hex = value.AsSpan(1);
        int r, g, b, a;
        switch (hex.Length)
        {
            case 3:
                if (!NibbleByte(hex[0], out r) || !NibbleByte(hex[1], out g) || !NibbleByte(hex[2], out b)) return false;
                a = 255;
                break;
            case 4:
                if (!NibbleByte(hex[0], out r) || !NibbleByte(hex[1], out g) || !NibbleByte(hex[2], out b) || !NibbleByte(hex[3], out a)) return false;
                break;
            case 6:
                if (!ByteOf(hex, 0, out r) || !ByteOf(hex, 2, out g) || !ByteOf(hex, 4, out b)) return false;
                a = 255;
                break;
            case 8:
                if (!ByteOf(hex, 0, out r) || !ByteOf(hex, 2, out g) || !ByteOf(hex, 4, out b) || !ByteOf(hex, 6, out a)) return false;
                break;
            default:
                return false;
        }

        argb = unchecked((a << 24) | (r << 16) | (g << 8) | b);
        return true;
    }

    /// <summary>解析 CSS 色值文本：hex（同 <see cref="TryParseHex"/>）或关键字 <c>transparent</c>/<c>none</c>（= 全透明）。</summary>
    public static bool TryParseCssColor(string? text, out int argb) => TryParseColor(text, out argb);

    /// <summary>
    /// 解析通用色值文本（**唯一实现**）：<c>transparent</c>/<c>none</c> · hex · <c>rgb()</c>/<c>rgba()</c> · 裸 <c>r,g,b[,a]</c>。
    /// <para>命名色不在此层（本程序集 BCL-only）；载体侧以 <c>Color.FromName</c> 兜底。</para>
    /// </summary>
    /// <param name="text">色值文本（可含首尾空白）。</param>
    /// <param name="argb">打包 ARGB（<c>0xAARRGGBB</c>）。</param>
    public static bool TryParseColor(string? text, out int argb)
    {
        argb = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var value = text.Trim();
        if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase)
            || value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value[0] == '#') return TryParseHex(value, out argb);
        if (TryParseRgbFunction(value, out argb)) return true;
        return TryParseBareRgb(value, out argb);
    }

    /// <summary>解析 <c>rgb(r,g,b)</c> / <c>rgba(r,g,b,a)</c>（a 支持 0–1 小数与 0–255 整数）。</summary>
    /// <param name="text">色值文本。</param>
    /// <param name="argb">打包 ARGB。</param>
    public static bool TryParseRgbFunction(string? text, out int argb)
    {
        argb = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var value = text.Trim();
        var open = value.IndexOf('(');
        if (open <= 0 || !value.EndsWith(')')) return false;

        var name = value[..open].Trim();
        var hasAlpha = name.Equals("rgba", StringComparison.OrdinalIgnoreCase);
        if (!hasAlpha && !name.Equals("rgb", StringComparison.OrdinalIgnoreCase)) return false;

        var parts = value[(open + 1)..^1].Split(
            new[] { ',', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 3 or > 4) return false;
        if (!int.TryParse(parts[0], out var r)
            || !int.TryParse(parts[1], out var g)
            || !int.TryParse(parts[2], out var b))
        {
            return false;
        }

        var alpha = 255;
        if (parts.Length == 4 && !TryParseAlpha(parts[3], out alpha)) return false;
        if (r is < 0 or > 255 || g is < 0 or > 255 || b is < 0 or > 255) return false;

        argb = unchecked((alpha << 24) | (r << 16) | (g << 8) | b);
        return true;
    }

    private static bool TryParseBareRgb(string value, out int argb)
    {
        argb = 0;
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is not (3 or 4)) return false;
        if (!int.TryParse(parts[0], out var r)
            || !int.TryParse(parts[1], out var g)
            || !int.TryParse(parts[2], out var b))
        {
            return false;
        }

        var alpha = 255;
        if (parts.Length == 4 && !int.TryParse(parts[3], out alpha)) return false;
        if (r is < 0 or > 255 || g is < 0 or > 255 || b is < 0 or > 255 || alpha is < 0 or > 255) return false;

        argb = unchecked((alpha << 24) | (r << 16) | (g << 8) | b);
        return true;
    }

    private static bool TryParseAlpha(string text, out int alpha)
    {
        alpha = 255;
        if (!double.TryParse(
                text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed))
        {
            return false;
        }

        alpha = parsed <= 1.0
            ? (int)Math.Round(parsed * 255, MidpointRounding.AwayFromZero)
            : (int)Math.Round(parsed, MidpointRounding.AwayFromZero);
        return alpha is >= 0 and <= 255;
    }

    /// <summary>打包 ARGB → CSS hex（固定 <c>#RRGGBBAA</c>，alpha 最后）。</summary>
    public static string ToCssHex(int argb)
    {
        var a = (argb >> 24) & 0xFF;
        var r = (argb >> 16) & 0xFF;
        var g = (argb >> 8) & 0xFF;
        var b = argb & 0xFF;
        return "#" + ByteHex(r) + ByteHex(g) + ByteHex(b) + ByteHex(a);
    }

    private static string ByteHex(int value) => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture);

    private static bool Nibble(char c, out int value)
    {
        if (c >= '0' && c <= '9') { value = c - '0'; return true; }
        if (c >= 'a' && c <= 'f') { value = c - 'a' + 10; return true; }
        if (c >= 'A' && c <= 'F') { value = c - 'A' + 10; return true; }
        value = 0;
        return false;
    }

    /// <summary>CSS 短 hex 的单个 nibble 按重复展开（<c>#07b</c> ⇒ <c>#0077bb</c>，即 <c>n * 0x11</c>）。</summary>
    private static bool NibbleByte(char c, out int value)
    {
        if (!Nibble(c, out var nibble))
        {
            value = 0;
            return false;
        }

        value = nibble * 0x11;
        return true;
    }

    private static bool ByteOf(ReadOnlySpan<char> hex, int offset, out int value)
    {
        if (!Nibble(hex[offset], out var high) || !Nibble(hex[offset + 1], out var low))
        {
            value = 0;
            return false;
        }

        value = (high << 4) | low;
        return true;
    }
}
