using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Share.LogicComposition.Conversion.Text;

/// <summary>
/// Markdown 正文的一节：<see cref="Heading"/> 为 `## ` 标题原文（前言节为空串），<see cref="Number"/> 为标题首数字段（如 `2` / `8.5`）。
/// </summary>
public sealed class MarkdownSection
{
    /// <summary>构造一节。</summary>
    /// <param name="heading">`## ` 标题原文（前言节为空串）。</param>
    /// <param name="number">标题首数字段（无数字为空串）。</param>
    /// <param name="text">节正文（不含标题行）。</param>
    public MarkdownSection(string heading, string number, string text)
    {
        Heading = heading;
        Number = number;
        Text = text;
    }

    /// <summary>`## ` 标题原文（前言节为空串）。</summary>
    public string Heading { get; }

    /// <summary>标题首数字段（如 `2` / `8.5`；无数字为空串）。</summary>
    public string Number { get; }

    /// <summary>节正文（不含标题行）。</summary>
    public string Text { get; }
}

/// <summary>
/// Markdown 分节原语（**节边界判定的唯一实现**，零外部依赖）。
/// <para>
/// 语义：节 = 行首 `## ` 起的块（`### ` 为节内子标题，不独立成节）；首个 `## ` 之前的内容为**前言节**（标题为空串）。
/// 围栏代码块（```）内的 `## ` 视为正文，不作分节边界。
/// </para>
/// </summary>
public static class MarkdownSections
{
    private const string Fence = "```";

    private static readonly Regex NumberPattern = new(@"^#{2,3}\s*(\d+(?:\.\d+)*)", RegexOptions.Compiled);

    /// <summary>按 `## ` 分节（前言节标题为空串；空正文 → 空列表）。</summary>
    /// <param name="body">Markdown 正文。</param>
    public static IReadOnlyList<MarkdownSection> Split(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return Array.Empty<MarkdownSection>();

        var lines = body!.Replace("\r\n", "\n").Split('\n');
        var sections = new List<MarkdownSection>();
        var heading = string.Empty;
        var number = string.Empty;
        var buffer = new List<string>();
        var inFence = false;

        void Flush()
        {
            var hasContent = false;
            foreach (var l in buffer)
            {
                if (l.Trim().Length > 0) { hasContent = true; break; }
            }

            if (heading.Length == 0 && !hasContent) return;
            sections.Add(new MarkdownSection(heading, number, string.Join("\n", buffer).Trim('\n')));
        }

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith(Fence, StringComparison.Ordinal))
            {
                inFence = !inFence;
                buffer.Add(line);
                continue;
            }

            if (!inFence && trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                heading = line.Trim();
                number = NumberOf(line);
                buffer = new List<string>();
                continue;
            }

            buffer.Add(line);
        }

        Flush();
        return sections;
    }

    /// <summary>全部 `## ` 节标题（不含前言），供 front-matter `sections:` 对账与索引生成。</summary>
    /// <param name="body">Markdown 正文。</param>
    public static IReadOnlyList<string> Headings(string? body)
        => Split(body).Where(s => s.Heading.Length > 0).Select(s => s.Heading).ToArray();

    /// <summary>节号列表（如 `2` / `8`），与 <see cref="Headings"/> 同序。</summary>
    /// <param name="body">Markdown 正文。</param>
    public static IReadOnlyList<string> Numbers(string? body)
        => Split(body).Where(s => s.Heading.Length > 0).Select(s => s.Number).ToArray();

    /// <summary>节号取值（`## 2. ③ …` → `2`；`### 8.5 x` → `8.5`；无数字 → 空串）。</summary>
    /// <param name="line">标题行。</param>
    public static string NumberOf(string line)
    {
        var match = NumberPattern.Match(line.Trim());
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    /// <summary>取节内 `### <paramref name="number"/> …` 子块（到下一个 `### ` 或节末；无此子标题 → 空串）。</summary>
    /// <param name="sectionText">节正文。</param>
    /// <param name="number">子标题节号（如 `8.5`）。</param>
    public static string SubText(string sectionText, string number)
    {
        var lines = sectionText.Replace("\r\n", "\n").Split('\n');
        var start = -1;

        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].TrimStart().StartsWith("### ", StringComparison.Ordinal)) continue;
            if (!string.Equals(NumberOf(lines[i]), number, StringComparison.Ordinal)) continue;
            start = i;
            break;
        }

        if (start < 0) return string.Empty;

        var end = lines.Length;
        for (var i = start + 1; i < lines.Length; i++)
        {
            if (!lines[i].TrimStart().StartsWith("### ", StringComparison.Ordinal)) continue;
            end = i;
            break;
        }

        var picked = new List<string>(end - start);
        for (var i = start; i < end; i++) picked.Add(lines[i]);
        return string.Join("\n", picked).Trim();
    }
}
