using System.Globalization;
using System.Text;

namespace Modules.Parsing.Markup;

/// <summary>
/// HTML 实体编解码（单实现）—— 与 <see cref="MarkupDialect.EncodeText"/> 成对，供"解析→语义解码→再序列化"幂等链使用。
/// <para>只识别命名字符与十进制/十六进制引用；不识别/超长实体原样保留（不猜测）。</para>
/// </summary>
public static class HtmlEntityCodec
{
    /// <summary>单遍 HTML 实体解码（命名字符 + 十进制/十六进制引用）。</summary>
    public static string Decode(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf('&') < 0) return value ?? string.Empty;
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '&') { sb.Append(value[i]); continue; }
            var semi = value.IndexOf(';', i + 1);
            if (semi < 0 || semi - i > 12) { sb.Append('&'); continue; }
            if (TryDecodeEntity(value.Substring(i + 1, semi - i - 1), out var decoded))
            {
                sb.Append(decoded);
                i = semi;
                continue;
            }
            sb.Append('&');
        }

        return sb.ToString();
    }

    private static bool TryDecodeEntity(string body, out string decoded)
    {
        switch (body)
        {
            case "amp": decoded = "&"; return true;
            case "lt": decoded = "<"; return true;
            case "gt": decoded = ">"; return true;
            case "quot": decoded = "\""; return true;
            case "apos": decoded = "'"; return true;
            case "nbsp": decoded = "\u00a0"; return true;
        }

        if (body.Length > 1 && body[0] == '#')
        {
            var isHex = body.Length > 2 && (body[1] == 'x' || body[1] == 'X');
            var digits = isHex ? body.Substring(2) : body.Substring(1);
            if (int.TryParse(digits, isHex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)
                && code > 0 && code <= 0x10FFFF)
            {
                decoded = char.ConvertFromUtf32(code);
                return true;
            }
        }

        decoded = string.Empty;
        return false;
    }
}
