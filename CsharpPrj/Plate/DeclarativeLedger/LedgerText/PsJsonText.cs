using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BasicMethod.Declarative.Ledger;

/// <summary>
/// 台账 JSON 文本序列化：与 **PowerShell <c>ConvertTo-Json -Depth N</c>** 逐字节同口径
/// （缩进 2 空格 / 换行 CRLF / 无尾换行 / 数值取原文 / 转义同 Newtonsoft 默认）。
/// <para>
/// 动机：离线写入器重组分片与产物时须产出与既有 PowerShell 工具**逐字节相同**的文本，
/// 才能用「字节对拍门」替代「人工核对」——差异即红，不靠人眼。键序 = 入参序（不排序）。
/// </para>
/// <para>
/// 口径来源（本机实测，非推测）：<c>"</c>→<c>\"</c>、<c>\</c>→<c>\\</c>、<c>\b \f \n \r \t</c>、
/// &lt;0x20 与 <c>U+0085 / U+2028 / U+2029</c> → <c>\uXXXX</c>（小写四位）；其余字符（含 CJK、
/// 代理对、<c>0x7F / 0x9F / 0xA0 / 0xAD / U+FEFF</c>）**原样输出**，不转义。
/// </para>
/// <para>
/// 已知口径差异（**显式登记，不静默**）：幂写法（如 <c>1e3</c>）PowerShell 规范化为 <c>1000.0</c>，
/// 本件按**原文保真**输出 <c>1e3</c>。台账实测无此类数值；一旦出现，字节对拍门当场报警。
/// </para>
/// </summary>
public static class PsJsonText
{
    /// <summary>行分隔（PowerShell 在 Windows 上的 <c>ConvertTo-Json</c> 口径；产物无尾换行）。</summary>
    public const string LineBreak = "\r\n";

    /// <summary>缩进单元（实测 2 空格）。</summary>
    private const string IndentUnit = "  ";

    /// <summary>单值渲染（<paramref name="continuationPrefix"/> = 非首行行前缀，供块级改写用）。</summary>
    public static string Write(JsonElement element, string continuationPrefix = "")
    {
        var text = new StringBuilder();
        AppendValue(text, element, 0);
        return Finish(text.ToString(), continuationPrefix);
    }

    /// <summary>有序成员表按对象渲染（成员序即入参序；空表 → <c>{}</c>）。</summary>
    public static string WriteMembers(
        IReadOnlyList<(string Name, JsonElement Value)> members, string continuationPrefix = "")
    {
        var text = new StringBuilder();
        AppendMembers(text, members, 0);
        return Finish(text.ToString(), continuationPrefix);
    }

    /// <summary>两段式文档：顶层有序成员 + 一个具名聚合段（段内有序成员）——台账产物与分片的共同形态。</summary>
    public static string WriteWithSection(
        IReadOnlyList<(string Name, JsonElement Value)> head,
        string sectionName,
        IReadOnlyList<(string Name, JsonElement Value)> section,
        string continuationPrefix = "")
    {
        var text = new StringBuilder();
        var leading = true;
        text.Append('{');
        foreach (var (name, value) in head)
        {
            AppendSeparator(text, ref leading);
            AppendMember(text, name, value, 0);
        }

        AppendSeparator(text, ref leading);
        text.Append(LineBreak).Append(Pad(1)).Append(Quote(sectionName)).Append(": ");
        AppendMembers(text, section, 1);
        text.Append(LineBreak).Append('}');
        return Finish(text.ToString(), continuationPrefix);
    }

    private static void AppendValue(StringBuilder text, JsonElement element, int depth)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                AppendMembers(
                    text,
                    [.. element.EnumerateObject().Select(property => (property.Name, property.Value))],
                    depth);
                return;

            case JsonValueKind.Array:
                if (element.GetArrayLength() == 0)
                {
                    text.Append("[]");
                    return;
                }

                text.Append('[');
                var leading = true;
                foreach (var item in element.EnumerateArray())
                {
                    AppendSeparator(text, ref leading);
                    text.Append(LineBreak).Append(Pad(depth + 1));
                    AppendValue(text, item, depth + 1);
                }

                text.Append(LineBreak).Append(Pad(depth)).Append(']');
                return;

            case JsonValueKind.String:
                text.Append(Quote(element.GetString() ?? string.Empty));
                return;

            case JsonValueKind.True:
                text.Append("true");
                return;

            case JsonValueKind.False:
                text.Append("false");
                return;

            case JsonValueKind.Number:
                text.Append(element.GetRawText());   // 数值取原文，避免 round-trip 变形
                return;

            default:                                  // Null / Undefined
                text.Append("null");
                return;
        }
    }

    private static void AppendMembers(
        StringBuilder text, IReadOnlyList<(string Name, JsonElement Value)> members, int depth)
    {
        if (members.Count == 0)
        {
            text.Append("{}");
            return;
        }

        text.Append('{');
        var leading = true;
        foreach (var (name, value) in members)
        {
            AppendSeparator(text, ref leading);
            AppendMember(text, name, value, depth);
        }

        text.Append(LineBreak).Append(Pad(depth)).Append('}');
    }

    private static void AppendMember(StringBuilder text, string name, JsonElement value, int depth)
    {
        text.Append(LineBreak).Append(Pad(depth + 1)).Append(Quote(name)).Append(": ");
        AppendValue(text, value, depth + 1);
    }

    private static void AppendSeparator(StringBuilder text, ref bool leading)
    {
        if (!leading) text.Append(',');
        leading = false;
    }

    private static string Pad(int depth) => new(' ', depth * IndentUnit.Length);

    private static string Quote(string value)
    {
        var text = new StringBuilder(value.Length + 2);
        text.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    text.Append("\\\"");
                    break;
                case '\\':
                    text.Append("\\\\");
                    break;
                case '\b':
                    text.Append("\\b");
                    break;
                case '\f':
                    text.Append("\\f");
                    break;
                case '\n':
                    text.Append("\\n");
                    break;
                case '\r':
                    text.Append("\\r");
                    break;
                case '\t':
                    text.Append("\\t");
                    break;
                default:
                    text.Append(NeedsUnicodeEscape(character)
                        ? "\\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture)
                        : character.ToString());
                    break;
            }
        }

        text.Append('"');
        return text.ToString();
    }

    /// <summary>需 <c>\uXXXX</c> 转义的字符：控制符（&lt;0x20）与 U+0085 / U+2028 / U+2029（行分隔族）。</summary>
    private static bool NeedsUnicodeEscape(char character)
        => character < 0x20 || character is '\u0085' or '\u2028' or '\u2029';

    private static string Finish(string body, string continuationPrefix)
    {
        if (string.IsNullOrEmpty(continuationPrefix)) return body;

        var lines = body.Split(LineBreak, StringSplitOptions.None);
        var text = new StringBuilder(body.Length + (continuationPrefix.Length * lines.Length));
        text.Append(lines[0]);
        for (var index = 1; index < lines.Length; index++)
            text.Append(LineBreak).Append(continuationPrefix).Append(lines[index]);

        return text.ToString();
    }
}
