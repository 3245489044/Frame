using System;
using System.Collections.Generic;
using System.Linq;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 入史标签词表（**单字段位掩码/集合**）—— 具名 token ⇔ <see cref="NodeHistoryMark"/> 位值的**唯一解析点**。
/// <para>
/// 语义：一个节点的标签 = 一个集合（单字段承载全部标签），标签可共存（如 <c>Terminal|Port</c>）；
/// 序列化形式 = 规范具名 token 串（分隔符 <c>|</c>）；<see cref="NodeHistoryMark.None"/> = 空集。
/// 标签名/位值的真源应在声明面（<c>Config\nodes\*.ushar</c> marks），本类只做 token ⇔ 位值的归一。
/// </para>
/// </summary>
public static class NodeTags
{
    /// <summary>不入史（空集）。</summary>
    public const string None = "None";

    /// <summary>编号中间节点。</summary>
    public const string Numbered = "Numbered";

    /// <summary>终答节点。</summary>
    public const string Terminal = "Terminal";

    /// <summary>task↔history 连接端口节点（可与其他标签共存）。</summary>
    public const string Port = "Port";

    private static readonly char[] Separators = { '|', ',', ';' };

    private static INodeTagVocabulary _vocabulary = DefaultNodeTagVocabulary.Instance;

    /// <summary>注入声明面词表（名称↔位值真源）；<c>null</c> ⇒ 回落内置缺省（零行为变更）。</summary>
    public static void Install(INodeTagVocabulary? vocabulary)
        => _vocabulary = vocabulary ?? DefaultNodeTagVocabulary.Instance;

    /// <summary>当前词表（取证 / 测试）。</summary>
    public static INodeTagVocabulary Vocabulary => _vocabulary;

    /// <summary>具名 token 集归一（分隔符 <c>|</c>/<c>,</c>/<c>;</c>；空/None ⇒ "None"；含未识别 token ⇒ null = 拒绝）。</summary>
    /// <param name="mark">原始标签（单 token 或 token 集）。</param>
    public static string? Normalize(string? mark)
    {
        var text = (mark ?? string.Empty).Trim();
        if (text.Length == 0 || text.Equals(None, StringComparison.OrdinalIgnoreCase)) return None;

        var parts = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var seen = new List<string>();
        foreach (var part in parts)
        {
            var canonical = Canonical(part);
            if (canonical is null) return null;
            if (!seen.Contains(canonical, StringComparer.Ordinal)) seen.Add(canonical);
        }

        return seen.Count == 0 ? None : string.Join("|", seen);
    }

    /// <summary>是否含某标签（集合语义，大小写不敏感）。</summary>
    public static bool Contains(string? mark, string tag)
        => (mark ?? string.Empty)
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));

    /// <summary>是否空集（空串/None）。</summary>
    public static bool IsEmpty(string? mark)
        => string.IsNullOrWhiteSpace(mark) || string.Equals(mark.Trim(), None, StringComparison.OrdinalIgnoreCase);

    /// <summary>是否含 Port 标签（task↔history 连接端口节点；集合语义）。</summary>
    public static bool IsPort(string? mark) => Contains(mark, Port);

    /// <summary>具名 token 集 → 位掩码（None/未识别 ⇒ <see cref="NodeHistoryMark.None"/>）。</summary>
    public static NodeHistoryMark Parse(string? mark)
    {
        var normalized = Normalize(mark);
        if (normalized is null || normalized == None) return NodeHistoryMark.None;

        var bits = 0;
        foreach (var name in normalized.Split('|'))
        {
            if (_vocabulary.Marks.TryGetValue(name, out var bit)) bits |= bit;
        }

        return (NodeHistoryMark)bits;
    }

    /// <summary>位掩码 → 规范具名 token 串（空集 ⇒ "None"）。</summary>
    public static string Serialize(NodeHistoryMark mark)
    {
        var bits = (int)mark;
        if (bits == 0) return None;
        var names = _vocabulary.Marks
            .Where(p => p.Value != 0 && (bits & p.Value) != 0)
            .OrderBy(p => p.Value)
            .Select(p => p.Key)
            .ToList();
        return names.Count == 0 ? None : string.Join("|", names);
    }

    private static string? Canonical(string token)
    {
        if (token.Equals(None, StringComparison.OrdinalIgnoreCase)) return None;
        foreach (var name in _vocabulary.Marks.Keys)
        {
            if (name.Equals(token, StringComparison.OrdinalIgnoreCase)) return name;
        }

        return null;
    }
}
