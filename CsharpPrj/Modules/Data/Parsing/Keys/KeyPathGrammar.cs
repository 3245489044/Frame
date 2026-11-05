using System;
using System.Collections.Generic;
using System.Text;

namespace Modules.Parsing.Keys;

/// <summary>v2 路径段类别（<see cref="KeyPathGrammar"/> 段模型）：成员 / 下标 / 通配。</summary>
public enum PathSegmentKind
{
    /// <summary>成员段（受转义约束的名称）。</summary>
    Member,

    /// <summary>数组下标段（<c>[digits]</c>）。</summary>
    Index,

    /// <summary>通配段（<c>*</c> 单层 / <c>**</c> 递归）。</summary>
    Wildcard,
}

/// <summary>v2 路径段（段序即位置语义）。</summary>
public sealed record PathSegment(PathSegmentKind Kind, string Name, int Index, bool Recursive);

/// <summary>
/// USharp 键文法 v2 · 段模型（K1/K4/K7 内核）：<c>Key ::= Segment (Separator Segment)*</c>，
/// 段 = Member | Index | Wildcard；Member 支持 <c>\</c> 转义（Reserved 字符）。
/// <para>取代朴素 <c>split('.')</c>（W1 无转义 / W4 下标 ad hoc / W5 无通配 / W6 无规范化）；
/// 构造恒出 canonical 形，反解 tolerant（未转义旧键按首分隔定位）。本类为**路径解析唯一实现**。</para>
/// </summary>
public static class KeyPathGrammar
{
    /// <summary>保留字符（成员段中须转义）。</summary>
    public const string Reserved = ".[]{}:\\*";

    /// <summary>解析路径为段序列（空段跳过；<c>**</c> / <c>*</c> 识别为通配）。</summary>
    public static IReadOnlyList<PathSegment> Parse(string? path)
    {
        var segments = new List<PathSegment>();
        if (string.IsNullOrEmpty(path)) return segments;

        var buffer = new StringBuilder();
        var i = 0;
        while (i < path.Length)
        {
            var c = path[i];

            if (c == '\\' && i + 1 < path.Length)
            {
                buffer.Append(path[i + 1]);
                i += 2;
                continue;
            }

            if (c == '.')
            {
                FlushMember(segments, buffer);
                i++;
                continue;
            }

            if (c == '[')
            {
                FlushMember(segments, buffer);
                var close = path.IndexOf(']', i + 1);
                if (close > i + 1 && int.TryParse(path.AsSpan(i + 1, close - i - 1), out var idx) && idx >= 0)
                {
                    segments.Add(new PathSegment(PathSegmentKind.Index, string.Empty, idx, false));
                    i = close + 1;
                }
                else
                {
                    buffer.Append(c); // 非法下标按字面量成员 tolerant
                    i++;
                }
                continue;
            }

            if (c == '*' && buffer.Length == 0)
            {
                if (i + 1 < path.Length && path[i + 1] == '*')
                {
                    segments.Add(new PathSegment(PathSegmentKind.Wildcard, "**", -1, true));
                    i += 2;
                }
                else
                {
                    segments.Add(new PathSegment(PathSegmentKind.Wildcard, "*", -1, false));
                    i += 1;
                }
                continue;
            }

            buffer.Append(c);
            i++;
        }

        FlushMember(segments, buffer);
        return segments;
    }

    /// <summary>段序列 → canonical 路径文本（成员转义保留字符；下标 <c>[n]</c>；通配原样）。</summary>
    public static string Render(IEnumerable<PathSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var sb = new StringBuilder();
        var first = true;
        foreach (var s in segments)
        {
            if (!first) sb.Append('.');
            first = false;
            switch (s.Kind)
            {
                case PathSegmentKind.Index:
                    sb.Append('[').Append(s.Index).Append(']');
                    break;
                case PathSegmentKind.Wildcard:
                    sb.Append(s.Recursive ? "**" : "*");
                    break;
                default:
                    sb.Append(Escape(s.Name));
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>成员名转义（保留字符前置 <c>\</c>）。</summary>
    public static string Escape(string name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (Reserved.IndexOf(c) >= 0) sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>路径 canonical 往返幂等判据（<c>Parse(Render(x)) == x</c>）。</summary>
    public static bool IsRoundTripStable(IReadOnlyList<PathSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var reparsed = Parse(Render(segments));
        if (reparsed.Count != segments.Count) return false;
        for (var i = 0; i < segments.Count; i++)
            if (reparsed[i] != segments[i]) return false;
        return true;
    }

    /// <summary>
    /// 选择器匹配（K7 批量支柱）：模式（含 <c>*</c>/<c>**</c>）是否命中键。
    /// <c>*</c> 匹配一段；<c>**</c> 匹配零或多段（尾部）。
    /// </summary>
    public static bool IsMatch(IReadOnlyList<PathSegment> pattern, IReadOnlyList<PathSegment> key)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(key);
        return MatchAt(pattern, 0, key, 0);
    }

    private static bool MatchAt(IReadOnlyList<PathSegment> pattern, int pi, IReadOnlyList<PathSegment> key, int ki)
    {
        while (pi < pattern.Count)
        {
            var p = pattern[pi];
            if (p.Kind == PathSegmentKind.Wildcard)
            {
                if (p.Recursive) return true; // ** 命中剩余任意（含空）
                if (ki >= key.Count) return false;
                pi++;
                ki++;
                continue;
            }

            if (ki >= key.Count) return false;
            if (p.Kind != key[ki].Kind) return false;
            if (p.Kind == PathSegmentKind.Index && p.Index != key[ki].Index) return false;
            if (p.Kind == PathSegmentKind.Member && !string.Equals(p.Name, key[ki].Name, StringComparison.Ordinal)) return false;
            pi++;
            ki++;
        }

        return ki == key.Count;
    }

    private static void FlushMember(List<PathSegment> segments, StringBuilder buffer)
    {
        if (buffer.Length == 0) return;
        segments.Add(new PathSegment(PathSegmentKind.Member, buffer.ToString(), -1, false));
        buffer.Clear();
    }
}
