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
    public static bool TryParseCssColor(string? text, out int argb)
    {
        var value = text?.Trim();
        if (value is not null && (value.Equals("transparent", StringComparison.OrdinalIgnoreCase)
                                  || value.Equals("none", StringComparison.OrdinalIgnoreCase)))
        {
            argb = 0;
            return true;
        }

        return TryParseHex(value, out argb);
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
