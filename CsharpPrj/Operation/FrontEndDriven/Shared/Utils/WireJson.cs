using System;
using System.Text.Json;

namespace FrontEndDriven.Shared.Utils;

/// <summary>
/// wire → JSON 的纯文本解码（唯一实现）：取最后一条 assistant 内容、剥代码围栏、取第一个平衡 {...}。
/// <para>供按名调用 Agent 的提案来源共用，插件侧不再各自复制一套。</para>
/// </summary>
public static class WireJson
{
    /// <summary>从 wire（消息数组）或裸文本中提取第一个 JSON 对象原文；失败返回 null。</summary>
    public static string? ExtractObject(string? wire)
    {
        if (string.IsNullOrWhiteSpace(wire)) return null;
        var content = LastAssistantContent(wire) ?? wire;
        return FirstBalancedObject(StripFences(content));
    }

    /// <summary>取 wire 中最后一条 assistant 的 content；非 wire 形状返回 null（交由上层用原文兜底）。</summary>
    public static string? LastAssistantContent(string? wire)
    {
        if (string.IsNullOrWhiteSpace(wire)) return null;
        try
        {
            using var doc = JsonDocument.Parse(wire);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            string? last = null;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var role = item.TryGetProperty("role", out var r) && r.ValueKind == JsonValueKind.String
                    ? r.GetString()
                    : null;
                if (!string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)) continue;
                if (item.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                    last = c.GetString();
            }
            return last;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>剥 ``` 代码围栏（支持 ```json 前缀）。</summary>
    public static string StripFences(string text)
    {
        var t = text.Trim();
        if (!t.StartsWith("```", StringComparison.Ordinal)) return t;

        var firstBreak = t.IndexOf('\n');
        t = firstBreak >= 0 ? t[(firstBreak + 1)..] : t[3..];
        var close = t.LastIndexOf("```", StringComparison.Ordinal);
        if (close >= 0) t = t[..close];
        return t.Trim();
    }

    /// <summary>取第一个平衡的 {...}（忽略字符串内的花括号与转义）。无 → null。</summary>
    public static string? FirstBalancedObject(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var start = text.IndexOf('{');
        if (start < 0) return null;

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (inString)
            {
                if (escaped) { escaped = false; continue; }
                if (ch == '\\') { escaped = true; continue; }
                if (ch == '"') inString = false;
                continue;
            }

            switch (ch)
            {
                case '"': inString = true; break;
                case '{': depth++; break;
                case '}':
                    depth--;
                    if (depth == 0) return text.Substring(start, i - start + 1);
                    break;
            }
        }
        return null;
    }
}
