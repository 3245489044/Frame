using System;
using System.Collections.Generic;
using System.Text;

namespace Modules.Parsing.Parsing;

/// <summary>
/// 命令行 key=value 参数解析（D6 单一实现）：空格分词、单/双引号包裹、值内引号保留。
/// 契约：整词被引号完整包裹 → 记为 <c>value</c>（脚本 value 语义）；否则按首个 '=' 拆 key/value。
/// 消费方：ScriptRunner（MetaCommandExtensions）与 HtmlScriptBridge（exec 参数）共用本实现。
/// </summary>
public static class KeyValueArguments
{
    public static Dictionary<string, string> Parse(string? value)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
            return result;

        foreach (var token in Tokenize(value))
        {
            var trimmed = token.Trim();
            if (trimmed.Length == 0)
                continue;

            if (trimmed.Length >= 2
                && (trimmed[0] == '"' || trimmed[0] == '\'')
                && trimmed[trimmed.Length - 1] == trimmed[0])
            {
                result["value"] = trimmed[1..^1];
                continue;
            }

            var eq = trimmed.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = trimmed[..eq].Trim();
            var val = trimmed[(eq + 1)..].Trim().Trim('"', '\'');
            if (key.Length > 0)
                result[key] = val;
        }

        return result;
    }

    public static Dictionary<string, object?> ParseObjects(string? value)
    {
        var parsed = Parse(value);
        var result = new Dictionary<string, object?>(parsed.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in parsed)
            result[kvp.Key] = kvp.Value;
        return result;
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuote = false;
        var quoteChar = '"';

        foreach (var ch in value)
        {
            if (ch == '"' || ch == '\'')
            {
                if (inQuote && ch == quoteChar)
                {
                    inQuote = false;
                    current.Append(ch);
                }
                else if (!inQuote)
                {
                    inQuote = true;
                    quoteChar = ch;
                    current.Append(ch);
                }
                else
                {
                    current.Append(ch);
                }
            }
            else if (ch == ' ' && !inQuote)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }
}
