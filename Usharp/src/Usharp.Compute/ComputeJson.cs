using System.Globalization;
using System.Text;

namespace Usharp.Compute;

/// <summary>
/// 极简规范化 JSON 树：对象成员键按 Ordinal 排序输出，数值只接受整数字面量或已规范化的十进制字符串，
/// 因此同一语义输入必然产出同一字节序列（无时间戳/无 GUID/无文化敏感格式）。
/// </summary>
internal abstract class CjNode
{
}

internal sealed class CjObject : CjNode
{
    public Dictionary<string, CjNode> Members { get; } = new(StringComparer.Ordinal);

    public CjObject Add(string key, CjNode value)
    {
        Members[key] = value;
        return this;
    }

    public CjObject Add(string key, string value) => Add(key, Cj.Str(value));
    public CjObject Add(string key, long value) => Add(key, Cj.Num(value));
    public CjObject Add(string key, bool value) => Add(key, new CjBool(value));
    public CjObject Add(string key, CjObject value) => Add(key, (CjNode)value);
    public CjObject Add(string key, CjArray value) => Add(key, (CjNode)value);
    public CjObject AddIf(string key, bool condition, CjNode value) => condition ? Add(key, value) : this;
    public CjObject AddIf(string key, bool condition, string value) => condition ? Add(key, value) : this;
}

internal sealed class CjArray : CjNode
{
    public List<CjNode> Items { get; } = new();

    public CjArray Add(CjNode item)
    {
        Items.Add(item);
        return this;
    }

    public CjArray Add(CjObject item) => Add((CjNode)item);
    public CjArray Add(string item) => Add(Cj.Str(item));
    public CjArray Add(long item) => Add(Cj.Num(item));
}

internal sealed class CjString : CjNode
{
    public string Value { get; }

    public CjString(string value) => Value = value;
}

internal sealed class CjNumber : CjNode
{
    public string Literal { get; }

    public CjNumber(string literal) => Literal = literal;
}

internal sealed class CjBool : CjNode
{
    public bool Value { get; }

    public CjBool(bool value) => Value = value;
}

internal static class Cj
{
    public static CjObject Obj() => new();
    public static CjArray Arr() => new();
    public static CjString Str(string value) => new(value);
    public static CjNumber Num(long value) => new(value.ToString(CultureInfo.InvariantCulture));
    public static CjNumber Dec(decimal value) => new(value.ToString("0.000000", CultureInfo.InvariantCulture));
}

internal static class CjWriter
{
    public static string Write(CjNode node)
    {
        var builder = new StringBuilder();
        WriteNode(builder, node);
        return builder.ToString();
    }

    private static void WriteNode(StringBuilder builder, CjNode node)
    {
        switch (node)
        {
            case CjObject obj:
                builder.Append('{');
                var first = true;
                foreach (var key in obj.Members.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    if (!first) builder.Append(',');
                    first = false;
                    WriteString(builder, key);
                    builder.Append(':');
                    WriteNode(builder, obj.Members[key]);
                }

                builder.Append('}');
                break;
            case CjArray array:
                builder.Append('[');
                for (var i = 0; i < array.Items.Count; i++)
                {
                    if (i > 0) builder.Append(',');
                    WriteNode(builder, array.Items[i]);
                }

                builder.Append(']');
                break;
            case CjString str:
                WriteString(builder, str.Value);
                break;
            case CjNumber number:
                builder.Append(number.Literal);
                break;
            case CjBool boolean:
                builder.Append(boolean.Value ? "true" : "false");
                break;
            default:
                builder.Append("null");
                break;
        }
    }

    private static void WriteString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else builder.Append(c);
                    break;
            }
        }

        builder.Append('"');
    }
}
