namespace Plate.DslMethod.Parsing;

/// <summary>
/// 字段解析器：从 `|` 分隔的 DSL 声明尾部提取结构化 `k=v` 字段。
/// 语义对齐 dsl.md：`|` 为合取/注释分隔；含 `=` 的分段视为结构化字段，否则为散文说明。
/// </summary>
public static class FieldParser
{
    /// <summary>按 `|` 切分声明尾部。</summary>
    public static string[] SplitOnPipe(string text)
        => text.Split('|');

    /// <summary>从分段中提取 `k=v` 字段（无 `=` 或格式非法者忽略）。</summary>
    public static List<KeyValuePair<string, string>> Parse(string[] parts)
        => Parse(parts, out _);

    /// <summary>从分段中提取 `k=v` 字段，并捕获首个无 `=` 分段作为声明正文 body。</summary>
    public static List<KeyValuePair<string, string>> Parse(string[] parts, out string body)
    {
        body = "";
        var fields = new List<KeyValuePair<string, string>>();
        foreach (var part in parts)
        {
            var p = part.Trim();
            if (p.Length == 0) continue;
            var eq = p.IndexOf('=');
            if (eq > 0)
                fields.Add(new KeyValuePair<string, string>(p[..eq].Trim(), p[(eq + 1)..].Trim()));
            else if (body.Length == 0)
                body = p;
        }
        return fields;
    }

    /// <summary>尝试把单个分段解析为字段；非字段返回 null。</summary>
    public static KeyValuePair<string, string>? TryParsePart(string part)
    {
        var p = part.Trim();
        var eq = p.IndexOf('=');
        if (eq <= 0) return null;
        return new KeyValuePair<string, string>(p[..eq].Trim(), p[(eq + 1)..].Trim());
    }
}