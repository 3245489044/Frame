using System;
using System.Collections.Generic;
using System.Text;

namespace Share.LogicComposition.Conversion.Validation.Rules;

/// <summary>作用域段：<c>name</c>（单实例）或 <c>name[*]</c>（集合逐元素）。</summary>
public sealed class RuleSpecScopeSegment
{
    /// <summary>段名（IR 键）。</summary>
    public string Name { get; }

    /// <summary>是否集合逐元素（文法 <c>name[*]</c>）。</summary>
    public bool IsCollection { get; }

    /// <summary>构筑作用域段。</summary>
    /// <param name="name">段名。</param>
    /// <param name="isCollection">是否集合逐元素。</param>
    public RuleSpecScopeSegment(string name, bool isCollection)
    {
        Name = name ?? string.Empty;
        IsCollection = isCollection;
    }

    /// <summary>规范化文本（<c>name</c> / <c>name[*]</c>）。</summary>
    public override string ToString() => IsCollection ? Name + "[*]" : Name;
}

/// <summary>
/// 规则判定作用域（文法：<c>root</c> | <c>seg(.seg)*</c>，<c>seg</c> = <c>name</c> | <c>name[*]</c>）。
/// <para>
/// 作用域是**纯文法**：本类型只做解析与形状校验（非法 ⇒ 构筑期硬失败），不承载求值；
/// 遍历/取值由 <c>IRuleObjectModel</c> 端口 + <c>RuleSpecEngine</c> 完成。
/// </para>
/// </summary>
public sealed class RuleSpecScope
{
    /// <summary>根作用域（<c>root</c>；仅 1 个实例）。</summary>
    public static readonly RuleSpecScope Root = new RuleSpecScope("root", Array.Empty<RuleSpecScopeSegment>());

    private static readonly IReadOnlyList<RuleSpecScopeSegment> NoSegments = Array.Empty<RuleSpecScopeSegment>();

    /// <summary>作用域段序（<c>root</c> 时为空）。</summary>
    public IReadOnlyList<RuleSpecScopeSegment> Segments { get; }

    /// <summary>是否根作用域。</summary>
    public bool IsRoot { get; }

    /// <summary>规范化作用域文本（原样保留声明写法，供诊断）。</summary>
    public string Text { get; }

    private RuleSpecScope(string text, IReadOnlyList<RuleSpecScopeSegment> segments)
    {
        Text = text;
        Segments = segments;
        IsRoot = segments.Count == 0;
    }

    /// <summary>
    /// 解析作用域文法；非法返回 false 并给出原因（构筑期硬失败用）。
    /// </summary>
    /// <param name="text">作用域文本。</param>
    /// <param name="scope">out：解析结果（失败为 null）。</param>
    /// <param name="error">out：失败原因（成功为 null）。</param>
    public static bool TryParse(string? text, out RuleSpecScope scope, out string? error)
    {
        scope = null!;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "作用域为空（须为 root 或 seg(.seg)*）";
            return false;
        }

        var trimmed = text!.Trim();
        if (string.Equals(trimmed, "root", StringComparison.Ordinal))
        {
            scope = Root;
            return true;
        }

        var rawSegments = trimmed.Split('.');
        var segments = new List<RuleSpecScopeSegment>(rawSegments.Length);
        for (var i = 0; i < rawSegments.Length; i++)
        {
            if (!TryParseSegment(rawSegments[i], out var segment, out var reason))
            {
                error = "作用域第 " + (i + 1) + " 段非法：" + reason + "（作用域=" + trimmed + "）";
                return false;
            }

            segments.Add(segment);
        }

        scope = new RuleSpecScope(trimmed, segments);
        return true;
    }

    /// <summary>解析单段（<c>name</c> / <c>name[*]</c>）。</summary>
    private static bool TryParseSegment(string raw, out RuleSpecScopeSegment segment, out string reason)
    {
        segment = null!;
        reason = string.Empty;

        var text = raw.Trim();
        if (text.Length == 0)
        {
            reason = "段为空";
            return false;
        }

        var isCollection = false;
        if (text.EndsWith("[*]", StringComparison.Ordinal))
        {
            isCollection = true;
            text = text.Substring(0, text.Length - 3).Trim();
        }
        else if (text.IndexOf('[') >= 0 || text.IndexOf(']') >= 0)
        {
            reason = "集合段只支持 [*] 形态（如 modules[*]）";
            return false;
        }

        if (!IsIdentifier(text))
        {
            reason = "段名 " + text + " 非法（须为 [A-Za-z_][A-Za-z0-9_-]*）";
            return false;
        }

        if (string.Equals(text, "root", StringComparison.Ordinal))
        {
            reason = "root 只能单独出现";
            return false;
        }

        if (PredicateCatalog.IsReservedKeyword(text))
        {
            reason = "段名 " + text + " 属受控控制流关键字（封闭文法禁止）";
            return false;
        }

        segment = new RuleSpecScopeSegment(text, isCollection);
        return true;
    }

    /// <summary>标识符判定（Ordinal；与词表白名单同口径）。</summary>
    private static bool IsIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        if (!IsIdentifierStart(value[0])) return false;

        for (var i = 1; i < value.Length; i++)
        {
            if (!IsIdentifierPart(value[i])) return false;
        }

        return true;
    }

    private static bool IsIdentifierStart(char c)
        => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_';

    private static bool IsIdentifierPart(char c)
        => IsIdentifierStart(c) || (c >= '0' && c <= '9') || c == '-';

    /// <summary>规范化文本（<c>root</c> 或段序拼接）。</summary>
    public override string ToString()
    {
        if (IsRoot) return "root";

        var builder = new StringBuilder();
        for (var i = 0; i < Segments.Count; i++)
        {
            if (i > 0) builder.Append('.');
            builder.Append(Segments[i]);
        }

        return builder.ToString();
    }
}
