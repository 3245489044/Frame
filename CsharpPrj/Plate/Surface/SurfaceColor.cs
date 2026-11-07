#nullable enable

using System;
using System.Collections.Generic;

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
    /// 解析通用色值文本（**唯一实现**）：<c>transparent</c>/<c>none</c> · hex · CSS 命名色 · <c>rgb()</c>/<c>rgba()</c> · 裸 <c>r,g,b[,a]</c>。
    /// <para>命名色按 CSS Color 4 关键字表（BCL-only；载体禁再用 <c>Color.FromName</c>，那会放行 token 并给出系统色差异）。</para>
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
        if (TryParseBareRgb(value, out argb)) return true;
        return TryParseNamed(value, out argb);
    }

    /// <summary>CSS 命名色（148 项，键为小写；值 = 打包 ARGB）。</summary>
    private static readonly IReadOnlyDictionary<string, int> Named = new Dictionary<string, int>(148, StringComparer.Ordinal)
    {
        ["aliceblue"] = unchecked((int)0xFFF0F8FF), ["antiquewhite"] = unchecked((int)0xFFFAEBD7),
        ["aqua"] = unchecked((int)0xFF00FFFF), ["aquamarine"] = unchecked((int)0xFF7FFFD4),
        ["azure"] = unchecked((int)0xFFF0FFFF), ["beige"] = unchecked((int)0xFFF5F5DC),
        ["bisque"] = unchecked((int)0xFFFFE4C4), ["black"] = unchecked((int)0xFF000000),
        ["blanchedalmond"] = unchecked((int)0xFFFFEBCD), ["blue"] = unchecked((int)0xFF0000FF),
        ["blueviolet"] = unchecked((int)0xFF8A2BE2), ["brown"] = unchecked((int)0xFFA52A2A),
        ["burlywood"] = unchecked((int)0xFFDEB887), ["cadetblue"] = unchecked((int)0xFF5F9EA0),
        ["chartreuse"] = unchecked((int)0xFF7FFF00), ["chocolate"] = unchecked((int)0xFFD2691E),
        ["coral"] = unchecked((int)0xFFFF7F50), ["cornflowerblue"] = unchecked((int)0xFF6495ED),
        ["cornsilk"] = unchecked((int)0xFFFFF8DC), ["crimson"] = unchecked((int)0xFFDC143C),
        ["cyan"] = unchecked((int)0xFF00FFFF), ["darkblue"] = unchecked((int)0xFF00008B),
        ["darkcyan"] = unchecked((int)0xFF008B8B), ["darkgoldenrod"] = unchecked((int)0xFFB8860B),
        ["darkgray"] = unchecked((int)0xFFA9A9A9), ["darkgreen"] = unchecked((int)0xFF006400),
        ["darkgrey"] = unchecked((int)0xFFA9A9A9), ["darkkhaki"] = unchecked((int)0xFFBDB76B),
        ["darkmagenta"] = unchecked((int)0xFF8B008B), ["darkolivegreen"] = unchecked((int)0xFF556B2F),
        ["darkorange"] = unchecked((int)0xFFFF8C00), ["darkorchid"] = unchecked((int)0xFF9932CC),
        ["darkred"] = unchecked((int)0xFF8B0000), ["darksalmon"] = unchecked((int)0xFFE9967A),
        ["darkseagreen"] = unchecked((int)0xFF8FBC8F), ["darkslateblue"] = unchecked((int)0xFF483D8B),
        ["darkslategray"] = unchecked((int)0xFF2F4F4F), ["darkslategrey"] = unchecked((int)0xFF2F4F4F),
        ["darkturquoise"] = unchecked((int)0xFF00CED1), ["darkviolet"] = unchecked((int)0xFF9400D3),
        ["deeppink"] = unchecked((int)0xFFFF1493), ["deepskyblue"] = unchecked((int)0xFF00BFFF),
        ["dimgray"] = unchecked((int)0xFF696969), ["dimgrey"] = unchecked((int)0xFF696969),
        ["dodgerblue"] = unchecked((int)0xFF1E90FF), ["firebrick"] = unchecked((int)0xFFB22222),
        ["floralwhite"] = unchecked((int)0xFFFFFAF0), ["forestgreen"] = unchecked((int)0xFF228B22),
        ["fuchsia"] = unchecked((int)0xFFFF00FF), ["gainsboro"] = unchecked((int)0xFFDCDCDC),
        ["ghostwhite"] = unchecked((int)0xFFF8F8FF), ["gold"] = unchecked((int)0xFFFFD700),
        ["goldenrod"] = unchecked((int)0xFFDAA520), ["gray"] = unchecked((int)0xFF808080),
        ["green"] = unchecked((int)0xFF008000), ["greenyellow"] = unchecked((int)0xFFADFF2F),
        ["grey"] = unchecked((int)0xFF808080), ["honeydew"] = unchecked((int)0xFFF0FFF0),
        ["hotpink"] = unchecked((int)0xFFFF69B4), ["indianred"] = unchecked((int)0xFFCD5C5C),
        ["indigo"] = unchecked((int)0xFF4B0082), ["ivory"] = unchecked((int)0xFFFFFFF0),
        ["khaki"] = unchecked((int)0xFFF0E68C), ["lavender"] = unchecked((int)0xFFE6E6FA),
        ["lavenderblush"] = unchecked((int)0xFFFFF0F5), ["lawngreen"] = unchecked((int)0xFF7CFC00),
        ["lemonchiffon"] = unchecked((int)0xFFFFFACD), ["lightblue"] = unchecked((int)0xFFADD8E6),
        ["lightcoral"] = unchecked((int)0xFFF08080), ["lightcyan"] = unchecked((int)0xFFE0FFFF),
        ["lightgoldenrodyellow"] = unchecked((int)0xFFFAFAD2), ["lightgray"] = unchecked((int)0xFFD3D3D3),
        ["lightgreen"] = unchecked((int)0xFF90EE90), ["lightgrey"] = unchecked((int)0xFFD3D3D3),
        ["lightpink"] = unchecked((int)0xFFFFB6C1), ["lightsalmon"] = unchecked((int)0xFFFFA07A),
        ["lightseagreen"] = unchecked((int)0xFF20B2AA), ["lightskyblue"] = unchecked((int)0xFF87CEFA),
        ["lightslategray"] = unchecked((int)0xFF778899), ["lightslategrey"] = unchecked((int)0xFF778899),
        ["lightsteelblue"] = unchecked((int)0xFFB0C4DE), ["lightyellow"] = unchecked((int)0xFFFFFFE0),
        ["lime"] = unchecked((int)0xFF00FF00), ["limegreen"] = unchecked((int)0xFF32CD32),
        ["linen"] = unchecked((int)0xFFFAF0E6), ["magenta"] = unchecked((int)0xFFFF00FF),
        ["maroon"] = unchecked((int)0xFF800000), ["mediumaquamarine"] = unchecked((int)0xFF66CDAA),
        ["mediumblue"] = unchecked((int)0xFF0000CD), ["mediumorchid"] = unchecked((int)0xFFBA55D3),
        ["mediumpurple"] = unchecked((int)0xFF9370DB), ["mediumseagreen"] = unchecked((int)0xFF3CB371),
        ["mediumslateblue"] = unchecked((int)0xFF7B68EE), ["mediumspringgreen"] = unchecked((int)0xFF00FA9A),
        ["mediumturquoise"] = unchecked((int)0xFF48D1CC), ["mediumvioletred"] = unchecked((int)0xFFC71585),
        ["midnightblue"] = unchecked((int)0xFF191970), ["mintcream"] = unchecked((int)0xFFF5FFFA),
        ["mistyrose"] = unchecked((int)0xFFFFE4E1), ["moccasin"] = unchecked((int)0xFFFFE4B5),
        ["navajowhite"] = unchecked((int)0xFFFFDEAD), ["navy"] = unchecked((int)0xFF000080),
        ["oldlace"] = unchecked((int)0xFFFDF5E6), ["olive"] = unchecked((int)0xFF808000),
        ["olivedrab"] = unchecked((int)0xFF6B8E23), ["orange"] = unchecked((int)0xFFFFA500),
        ["orangered"] = unchecked((int)0xFFFF4500), ["orchid"] = unchecked((int)0xFFDA70D6),
        ["palegoldenrod"] = unchecked((int)0xFFEEE8AA), ["palegreen"] = unchecked((int)0xFF98FB98),
        ["paleturquoise"] = unchecked((int)0xFFAFEEEE), ["palevioletred"] = unchecked((int)0xFFDB7093),
        ["papayawhip"] = unchecked((int)0xFFFFEFD5), ["peachpuff"] = unchecked((int)0xFFFFDAB9),
        ["peru"] = unchecked((int)0xFFCD853F), ["pink"] = unchecked((int)0xFFFFC0CB),
        ["plum"] = unchecked((int)0xFFDDA0DD), ["powderblue"] = unchecked((int)0xFFB0E0E6),
        ["purple"] = unchecked((int)0xFF800080), ["rebeccapurple"] = unchecked((int)0xFF663399),
        ["red"] = unchecked((int)0xFFFF0000), ["rosybrown"] = unchecked((int)0xFFBC8F8F),
        ["royalblue"] = unchecked((int)0xFF4169E1), ["saddlebrown"] = unchecked((int)0xFF8B4513),
        ["salmon"] = unchecked((int)0xFFFA8072), ["sandybrown"] = unchecked((int)0xFFF4A460),
        ["seagreen"] = unchecked((int)0xFF2E8B57), ["seashell"] = unchecked((int)0xFFFFF5EE),
        ["sienna"] = unchecked((int)0xFFA0522D), ["silver"] = unchecked((int)0xFFC0C0C0),
        ["skyblue"] = unchecked((int)0xFF87CEEB), ["slateblue"] = unchecked((int)0xFF6A5ACD),
        ["slategray"] = unchecked((int)0xFF708090), ["slategrey"] = unchecked((int)0xFF708090),
        ["snow"] = unchecked((int)0xFFFFFAFA), ["springgreen"] = unchecked((int)0xFF00FF7F),
        ["steelblue"] = unchecked((int)0xFF4682B4), ["tan"] = unchecked((int)0xFFD2B48C),
        ["teal"] = unchecked((int)0xFF008080), ["thistle"] = unchecked((int)0xFFD8BFD8),
        ["tomato"] = unchecked((int)0xFFFF6347), ["turquoise"] = unchecked((int)0xFF40E0D0),
        ["violet"] = unchecked((int)0xFFEE82EE), ["wheat"] = unchecked((int)0xFFF5DEB3),
        ["white"] = unchecked((int)0xFFFFFFFF), ["whitesmoke"] = unchecked((int)0xFFF5F5F5),
        ["yellow"] = unchecked((int)0xFFFFFF00), ["yellowgreen"] = unchecked((int)0xFF9ACD32),
    };

    private static bool TryParseNamed(string value, out int argb)
        => Named.TryGetValue(value.ToLowerInvariant(), out argb);

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
