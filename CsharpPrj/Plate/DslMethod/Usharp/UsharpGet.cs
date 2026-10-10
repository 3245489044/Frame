using System;
using System.Collections.Generic;
using System.Text;

namespace Plate.DslMethod.Usharp;

/// <summary>
/// USharp 记录文本的**字段投影**（SQL 侧集中式转换层的路径取值内核，权威：USharp 纯化施工单 §1.6）。
/// <para>
/// 语义 ≡ <c>json_extract(doc, path)</c> 的标量取值：把 <c>[Record] &lt;id&gt;: …</c> 规范文本按 <c>$.field[.sub]</c> 定位，
/// 返回标量文本（字符串去引号、布尔返 true/false、数字返原文本；对象/列表/未命中 ⇒ null）。
/// </para>
/// <para>
/// 本类位于中立库（存储层可引用，不引 <c>Usharp.*</c>），仅覆盖**规范形**（字段行
/// <c>name = &lt;单行规范值&gt;</c>）所需的极小读取；完整值文法解析器仍在 <c>Usharp.Syntax</c>，此处不复制其全量逻辑。
/// </para>
/// </summary>
public static class UsharpGet
{
    /// <summary>取 <paramref name="doc"/> 中 <paramref name="path"/> 指向的标量文本（未命中/非标量 ⇒ null）。</summary>
    public static string? Extract(string? doc, string? path)
    {
        if (string.IsNullOrWhiteSpace(doc) || string.IsNullOrWhiteSpace(path)) return null;
        var segments = NormalizePath(path!);
        if (segments.Count == 0) return null;

        var current = StripHeader(doc!);
        for (var i = 0; i < segments.Count; i++)
        {
            var text = FindField(current, segments[i]);
            if (text is null) return null;
            if (i == segments.Count - 1) return ScalarText(text);
            var inner = UnwrapRecord(text);
            if (inner is null) return null;
            current = inner;
        }
        return null;
    }

    /// <summary>路径归一：去 <c>$</c> / <c>$.</c> 前缀，按 <c>.</c> 分段。</summary>
    private static List<string> NormalizePath(string path)
    {
        var trimmed = path.Trim();
        if (trimmed.StartsWith("$.", StringComparison.Ordinal)) trimmed = trimmed[2..];
        else if (trimmed.StartsWith("$", StringComparison.Ordinal)) trimmed = trimmed[1..];

        var segments = new List<string>();
        foreach (var s in trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            segments.Add(s);
        return segments;
    }

    /// <summary>去掉记录头行（<c>[Record] …</c>），返回字段块文本（无头 ⇒ 原文）。</summary>
    private static string StripHeader(string doc)
    {
        var lines = doc.Replace("\r\n", "\n").Split('\n');
        var start = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var t = lines[i].TrimStart();
            if (t.Length == 0) continue;
            start = t.StartsWith("[Record]", StringComparison.Ordinal) ? i + 1 : i;
            break;
        }
        return string.Join("\n", lines, start, lines.Length - start);
    }

    /// <summary>在字段块中取字段 <paramref name="name"/> 的值文本（未命中 ⇒ null）。支持多行块与单行嵌套记录体。</summary>
    private static string? FindField(string block, string name)
    {
        foreach (var entry in SplitFields(block))
        {
            var line = entry.Trim();
            if (line.Length == 0) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            if (!string.Equals(line[..eq].Trim(), name, StringComparison.Ordinal)) continue;
            return line[(eq + 1)..].Trim();
        }
        return null;
    }

    /// <summary>字段条目切分：多行块逐行；单行记录体按顶层 <c>,</c> 切（尊重 <c>{}</c>/<c>[]</c>/字符串）。</summary>
    private static IEnumerable<string> SplitFields(string block)
    {
        if (block.IndexOf('\n') >= 0)
        {
            foreach (var line in block.Split('\n'))
            {
                var t = line.Trim();
                if (t.Length > 0) yield return t;
            }
            yield break;
        }

        int depth = 0, start = 0;
        var inString = false;
        for (var i = 0; i < block.Length; i++)
        {
            var c = block[i];
            if (inString)
            {
                if (c == '\\') { i++; continue; }
                if (c == '"') inString = false;
                continue;
            }
            if (c == '"') { inString = true; continue; }
            if (c == '{' || c == '[') depth++;
            else if (c == '}' || c == ']') depth--;
            else if (c == ',' && depth == 0) { yield return block[start..i]; start = i + 1; }
        }
        if (start < block.Length) yield return block[start..];
    }

    /// <summary>剥外层 <c>{ … }</c>，返回内部字段块（非记录 ⇒ null）。</summary>
    private static string? UnwrapRecord(string text)
    {
        var t = text.Trim();
        return t.Length >= 2 && t[0] == '{' && t[^1] == '}' ? t[1..^1] : null;
    }

    /// <summary>标量文本：字符串去引号并解转义；bool/int/decimal 原样；null/记录/列表 ⇒ null。</summary>
    private static string? ScalarText(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return null;
        if (t[0] == '{' || t[0] == '[') return null;
        if (t == "null") return null;
        return t[0] == '"' ? Unquote(t) : t;
    }

    /// <summary>去双引号 + 反解转义（与 <see cref="UsharpScalar.Quote"/> 对偶）。</summary>
    private static string? Unquote(string quoted)
    {
        if (quoted.Length < 2 || quoted[^1] != '"') return null;
        var sb = new StringBuilder(quoted.Length - 2);
        for (var i = 1; i < quoted.Length - 1; i++)
        {
            var c = quoted[i];
            if (c != '\\') { sb.Append(c); continue; }
            if (i + 1 >= quoted.Length - 1) return null;
            switch (quoted[++i])
            {
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                default: return null;
            }
        }
        return sb.ToString();
    }
}
