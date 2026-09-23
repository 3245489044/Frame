using System;
using System.Collections.Generic;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.LogicComposition.Conversion.Validation.Rules;

/// <summary>规则路径段：<c>name</c> / <c>name[n]</c> / <c>name[*]</c>。</summary>
public sealed class RulePathSegment
{
    /// <summary>键名（数组索引/通配段为 null）。</summary>
    public string? Name { get; }

    /// <summary>索引（仅 <c>[n]</c>；否则 -1）。</summary>
    public int Index { get; }

    /// <summary>是否通配展开（<c>[*]</c>）。</summary>
    public bool IsWildcard { get; }

    /// <summary>构造键段。</summary>
    /// <param name="name">键名。</param>
    public RulePathSegment(string name)
    {
        Name = name ?? string.Empty;
        Index = -1;
    }

    /// <summary>构造索引/通配段。</summary>
    /// <param name="index">索引。</param>
    /// <param name="isWildcard">是否通配。</param>
    public RulePathSegment(int index, bool isWildcard)
    {
        Name = null;
        Index = index;
        IsWildcard = isWildcard;
    }

    /// <summary>是否为数组段（索引或通配）。</summary>
    public bool IsArrayStep => IsWildcard || Index >= 0;

    /// <summary>规范化文本。</summary>
    public override string ToString()
        => IsWildcard ? "[*]" : Index >= 0 ? "[" + Index + "]" : Name ?? string.Empty;
}

/// <summary>
/// 规则路径（IR 取值路径）：文法 <c>seg(.seg)*</c>，<c>seg = name([n]|[*])*</c>。
/// <para>
/// <b>唯一实现</b>：规则表谓词实参的路径解析与 IR 取值只在此处发生，禁止第二份 dot-path 解析（防抽象再生）。
/// </para>
/// <para>取值语义：缺失 / 类型不匹配 ⇒ **空集**（不抛异常，保持「全量失败可见」；由谓词的缺失语义决定判定）。</para>
/// </summary>
public sealed class RulePath
{
    private static readonly IReadOnlyList<ConversionNode> NoNodes = Array.Empty<ConversionNode>();

    /// <summary>路径段序。</summary>
    public IReadOnlyList<RulePathSegment> Segments { get; }

    /// <summary>规范化路径文本。</summary>
    public string Text { get; }

    private RulePath(string text, List<RulePathSegment> segments)
    {
        Text = text;
        Segments = segments;
    }

    /// <summary>解析路径文法；非法返回 false 并给出原因（构筑期硬失败用）。</summary>
    /// <param name="text">路径文本。</param>
    /// <param name="path">out：解析结果（失败为 null）。</param>
    /// <param name="error">out：失败原因（成功为 null）。</param>
    public static bool TryParse(string? text, out RulePath path, out string? error)
    {
        path = null!;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "路径为空";
            return false;
        }

        var trimmed = text!.Trim();
        var segments = new List<RulePathSegment>();
        var rawSegments = trimmed.Split('.');
        for (var i = 0; i < rawSegments.Length; i++)
        {
            if (!TryParseSegment(rawSegments[i], out var parsed, out var reason))
            {
                error = "路径 " + trimmed + " 第 " + (i + 1) + " 段非法：" + reason;
                return false;
            }

            segments.AddRange(parsed);
        }

        if (segments.Count == 0)
        {
            error = "路径 " + trimmed + " 未解析出任何段";
            return false;
        }

        path = new RulePath(trimmed, segments);
        return true;
    }

    /// <summary>是否含通配段（通配路径 = 集合语义，如 <c>modules[*].links[*]</c>）。</summary>
    public bool HasWildcard
    {
        get
        {
            for (var i = 0; i < Segments.Count; i++)
            {
                if (Segments[i].IsWildcard) return true;
            }

            return false;
        }
    }

    /// <summary>从节点出发解析全部取值（通配展开；缺失 ⇒ 空集）。</summary>
    /// <param name="root">起点节点（可空）。</param>
    public IReadOnlyList<ConversionNode> ResolveAll(ConversionNode? root)
    {
        if (root is null) return NoNodes;

        var current = new List<ConversionNode> { root };
        for (var i = 0; i < Segments.Count; i++)
        {
            var next = new List<ConversionNode>();
            var segment = Segments[i];
            for (var n = 0; n < current.Count; n++) Step(current[n], segment, next);

            if (next.Count == 0) return NoNodes;
            current = next;
        }

        return current;
    }

    /// <summary>从节点出发解析首个取值（缺失 ⇒ false）。</summary>
    /// <param name="root">起点节点（可空）。</param>
    /// <param name="value">out：首个取值。</param>
    public bool TryResolve(ConversionNode? root, out ConversionNode? value)
    {
        var all = ResolveAll(root);
        value = all.Count > 0 ? all[0] : null;
        return all.Count > 0;
    }

    /// <summary>规范化路径文本。</summary>
    public override string ToString() => Text;

    /// <summary>按段推进一层（缺失/类型不匹配静默跳过）。</summary>
    private static void Step(ConversionNode node, RulePathSegment segment, List<ConversionNode> target)
    {
        if (segment.IsArrayStep)
        {
            var items = Elements(node);
            if (segment.IsWildcard)
            {
                for (var i = 0; i < items.Count; i++) target.Add(items[i]);
            }
            else if (segment.Index >= 0 && segment.Index < items.Count)
            {
                target.Add(items[segment.Index]);
            }

            return;
        }

        if (node is ConversionObject obj && obj.TryGet(segment.Name!, out var member)) target.Add(member);
    }

    /// <summary>取节点的元素序列（数组 → 元素；对象 → 成员值；标量 → 空）。</summary>
    private static IReadOnlyList<ConversionNode> Elements(ConversionNode node)
    {
        switch (node)
        {
            case ConversionArray array:
                return array.Items;

            case ConversionObject obj:
                var values = new List<ConversionNode>(obj.Count);
                for (var i = 0; i < obj.Members.Count; i++) values.Add(obj.Members[i].Value);
                return values;

            default:
                return NoNodes;
        }
    }

    private static bool TryParseSegment(string raw, out List<RulePathSegment> segments, out string reason)
    {
        segments = new List<RulePathSegment>();
        reason = string.Empty;

        var text = raw.Trim();
        var bracketIndex = text.IndexOf('[');
        var name = bracketIndex < 0 ? text : text.Substring(0, bracketIndex).Trim();
        if (name.Length > 0) segments.Add(new RulePathSegment(name));

        var cursor = bracketIndex;
        while (cursor >= 0)
        {
            var close = text.IndexOf(']', cursor + 1);
            if (close < 0)
            {
                reason = "缺少右括号（" + text + "）";
                return false;
            }

            var inner = text.Substring(cursor + 1, close - cursor - 1).Trim();
            if (inner == "*")
            {
                segments.Add(new RulePathSegment(0, isWildcard: true));
            }
            else if (int.TryParse(inner, out var number) && number >= 0)
            {
                segments.Add(new RulePathSegment(number, isWildcard: false));
            }
            else
            {
                reason = "数组下标非法（" + inner + "；只支持 [n] 与 [*]）";
                return false;
            }

            cursor = text.IndexOf('[', close + 1);
        }

        if (segments.Count == 0)
        {
            reason = "段为空";
            return false;
        }

        return true;
    }
}
