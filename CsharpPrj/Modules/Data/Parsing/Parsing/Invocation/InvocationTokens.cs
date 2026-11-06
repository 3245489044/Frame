using System;
using System.Collections.Generic;
using System.Text;

namespace Modules.Parsing.Invocation;

/// <summary>
/// 目标串切分（唯一实现）：空白分隔 + 引号成组。
/// 命令层不再手写 Split(' ') / IndexOf(' ') / StartsWith("count")。
/// </summary>
public static class InvocationTokens
{
    public static IReadOnlyList<string> Tokenize(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return Array.Empty<string>();

        var tokens = new List<string>();
        var buffer = new StringBuilder();
        var quote = '\0';

        foreach (var ch in target)
        {
            if (quote != '\0')
            {
                if (ch == quote) quote = '\0';
                else buffer.Append(ch);
                continue;
            }

            if (ch is '"' or '\'')
            {
                quote = ch;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (buffer.Length > 0) { tokens.Add(buffer.ToString()); buffer.Clear(); }
                continue;
            }

            buffer.Append(ch);
        }

        if (buffer.Length > 0) tokens.Add(buffer.ToString());
        return tokens;
    }

    /// <summary>首 token（无则 null）。</summary>
    public static string? First(string? target)
    {
        var tokens = Tokenize(target);
        return tokens.Count == 0 ? null : tokens[0];
    }

    /// <summary>去掉首 token 后的剩余串（无剩余 ⇒ null）。</summary>
    public static string? Rest(string? target)
    {
        var tokens = Tokenize(target);
        if (tokens.Count <= 1) return null;
        var text = (target ?? string.Empty).Trim();
        var head = tokens[0];
        var at = text.IndexOf(head, StringComparison.Ordinal);
        if (at < 0) return null;
        var rest = text[(at + head.Length)..].Trim();
        return rest.Length == 0 ? null : rest;
    }

    /// <summary>
    /// 剥离子命令前缀后取剩余（兼容 <c>container ...</c> 与 <c>assert-container ...</c> 两种写法）。
    /// </summary>
    public static string? TailAfter(string? target, string subAction)
    {
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(subAction)) return null;

        var head = First(target);
        if (head is null) return null;

        if (string.Equals(head, subAction, StringComparison.OrdinalIgnoreCase))
            return Rest(target);

        if (string.Equals(head, "assert-" + subAction, StringComparison.OrdinalIgnoreCase))
            return Rest(target);

        return null;
    }

    /// <summary>第 n 个 token（越界 ⇒ null）。</summary>
    public static string? At(string? target, int position)
    {
        var tokens = Tokenize(target);
        return position >= 0 && position < tokens.Count ? tokens[position] : null;
    }
}
