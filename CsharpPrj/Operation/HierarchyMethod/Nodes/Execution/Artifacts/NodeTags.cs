using System;
using System.Collections.Generic;
using System.Linq;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 入史标签词表（**单字段位掩码/集合**）—— 具名 token ⇔ <see cref="ContainerMark"/> 位值的**唯一解析点**。
/// <para>
/// 语义：一个节点的标签 = 一个集合（单字段承载全部标签），标签可共存（如 <c>Terminal|Port</c>）；
/// 序列化形式 = 规范具名 token 串（分隔符 <c>|</c>）；<see cref="ContainerMark.None"/> = 空集。
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

    /// <summary>内置轴名（历史轴）：无前缀 token 视作本轴（存量零回归）。</summary>
    public const string HistoryAxis = "history";

    private static readonly char[] Separators = { '|', ',', ';' };

    /// <summary>token 分隔符（只读；全库唯一切分口径，其余调用点一律引用本属性）。</summary>
    public static char[] TokenSeparators => Separators;

    private static readonly TagVocabularyRegistry _registry = new();

    static NodeTags() => _registry.Register(HistoryAxis, DefaultNodeTagVocabulary.Instance);

    /// <summary>轴注册表（宿主/领域装配面按轴注册词表；轴级单源）。</summary>
    public static ITagVocabularyRegistry VocabularyRegistry => _registry;

    /// <summary>注入声明面词表（名称↔位值真源，注册到 <see cref="HistoryAxis"/> 轴）；<c>null</c> ⇒ 回落内置缺省（零行为变更）。
    /// <para>兼容壳：等价 <c>VocabularyRegistry.Register("history", …)</c>（标记待退役）。</para></summary>
    public static void Install(INodeTagVocabulary? vocabulary)
        => _registry.Register(HistoryAxis, vocabulary ?? DefaultNodeTagVocabulary.Instance);

    /// <summary>当前 history 轴词表（取证 / 测试）。</summary>
    public static INodeTagVocabulary Vocabulary
        => _registry.TryGet(HistoryAxis, out var vocabulary) ? vocabulary : DefaultNodeTagVocabulary.Instance;

    /// <summary>具名 token 集归一（分隔符 <c>|</c>/<c>,</c>/<c>;</c>；空/None ⇒ "None"；含未识别 token ⇒ null = 拒绝）。
    /// <para>token 形态：<c>axis.token</c> 全限定，或无前缀 = <c>history.</c>（存量兼容）；
    /// 输出保持 history 轴 token 无前缀（存量规范串逐字不变）。</para></summary>
    /// <param name="mark">原始标签（单 token 或 token 集）。</param>
    public static string? Normalize(string? mark)
    {
        var text = (mark ?? string.Empty).Trim();
        if (text.Length == 0 || text.Equals(None, StringComparison.OrdinalIgnoreCase)) return None;

        var parts = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var seen = new List<string>();
        foreach (var part in parts)
        {
            var (axis, local) = SplitToken(part);
            if (local.Equals(None, StringComparison.OrdinalIgnoreCase))
            {
                if (!seen.Contains(None, StringComparer.Ordinal)) seen.Add(None);
                continue;
            }

            var canonical = Canonical(axis, local);
            if (canonical is null) return null;

            var formatted = Format(axis, canonical);
            if (!seen.Contains(formatted, StringComparer.Ordinal)) seen.Add(formatted);
        }

        return seen.Count == 0 ? None : string.Join("|", seen);
    }

    /// <summary>单 token 归一（指定轴；未识别 ⇒ null）。history 轴输出无前缀，其余轴输出 <c>axis.token</c>。</summary>
    /// <param name="axis">轴名（如 <c>history</c>/<c>role</c>/<c>visibility</c>）。</param>
    /// <param name="token">轴内 token 名（不含轴前缀）。</param>
    public static string? Normalize(string axis, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(axis);
        var canonical = Canonical(axis, token);
        return canonical is null ? null : Format(axis, canonical);
    }

    /// <summary>是否含某标签（集合语义，大小写不敏感；bare/qualified 互为兼容）。</summary>
    public static bool Contains(string? mark, string tag)
        => (mark ?? string.Empty)
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(t =>
            {
                if (t.Equals(tag, StringComparison.OrdinalIgnoreCase)) return true;
                var (_, local) = SplitToken(t);
                return local.Equals(tag, StringComparison.OrdinalIgnoreCase);
            });

    /// <summary>是否含某轴内某 token（集合语义；等价 <see cref="Contains(string?, string)"/> 的具名轴口径）。</summary>
    public static bool Contains(string? mark, string axis, string token)
        => Contains(mark, Format(axis, token));

    /// <summary>是否含任一给定 token（集合语义，大小写不敏感；bare/qualified 互为兼容；空候选 ⇒ false）。</summary>
    public static bool ContainsAny(string? mark, IReadOnlyList<string> tags)
    {
        if (tags is null || tags.Count == 0) return false;
        foreach (var part in (mark ?? string.Empty)
                     .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (_, local) = SplitAxisToken(part);
            foreach (var tag in tags)
            {
                if (part.Equals(tag, StringComparison.OrdinalIgnoreCase)
                    || local.Equals(tag, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }

        return false;
    }

    /// <summary>是否含全部给定 token（合取语义；空候选 ⇒ true，空/无 token 的单侧不做判定，由调用方先行短路）。</summary>
    public static bool ContainsAll(string? mark, IReadOnlyList<string> tags)
    {
        if (tags is null || tags.Count == 0) return true;
        foreach (var tag in tags)
        {
            if (!Contains(mark, tag)) return false;
        }

        return true;
    }

    /// <summary>是否空集（空串/None）。</summary>
    public static bool IsEmpty(string? mark)
        => string.IsNullOrWhiteSpace(mark) || string.Equals(mark.Trim(), None, StringComparison.OrdinalIgnoreCase);

    /// <summary>是否含 Port 标签（task↔history 连接端口节点；集合语义）。</summary>
    public static bool IsPort(string? mark) => Contains(mark, Port);

    /// <summary>是否终答标签（**唯一终端谓词**；role/kind 派生等消费方一律经此，禁各自 <c>Contains(..., Terminal)</c>）。</summary>
    public static bool IsTerminal(string? mark) => Contains(mark, Terminal);

    /// <summary>具名 token 集 → 位掩码（仅 history 轴参与；None/未识别 ⇒ <see cref="ContainerMark.None"/>）。</summary>
    public static ContainerMark Parse(string? mark)
    {
        var normalized = Normalize(mark);
        if (normalized is null || normalized == None) return ContainerMark.None;

        var bits = 0;
        foreach (var name in normalized.Split('|'))
        {
            var (axis, local) = SplitToken(name);
            if (!axis.Equals(HistoryAxis, StringComparison.OrdinalIgnoreCase)) continue;
            if (_registry.TryResolve(HistoryAxis, local, out var bit)) bits |= bit;
        }

        return (ContainerMark)bits;
    }

    /// <summary>位掩码 → 规范具名 token 串（空集 ⇒ "None"；history 轴无前缀）。</summary>
    public static string Serialize(ContainerMark mark)
    {
        var bits = (int)mark;
        if (bits == 0) return None;
        var names = Vocabulary.Marks
            .Where(p => p.Value != 0 && (bits & p.Value) != 0)
            .OrderBy(p => p.Value)
            .Select(p => p.Key)
            .ToList();
        return names.Count == 0 ? None : string.Join("|", names);
    }

    private static string? Canonical(string axis, string token)
    {
        if (token.Equals(None, StringComparison.OrdinalIgnoreCase)) return None;
        if (!_registry.TryGet(axis, out var vocabulary)) return null;
        foreach (var name in vocabulary.Marks.Keys)
        {
            if (name.Equals(token, StringComparison.OrdinalIgnoreCase)) return name;
        }

        return null;
    }

    private static string Format(string axis, string canonical)
        => axis.Equals(HistoryAxis, StringComparison.OrdinalIgnoreCase) ? canonical : axis + "." + canonical;

    /// <summary>轴内 token 切分：<c>axis.token</c> ⇒ (axis, token)；无点 ⇒ (history, token)（**唯一切分实现**）。</summary>
    public static (string Axis, string Local) SplitAxisToken(string part)
    {
        var dot = part.IndexOf('.');
        return dot > 0 && dot < part.Length - 1
            ? (part[..dot], part[(dot + 1)..])
            : (HistoryAxis, part);
    }

    private static (string Axis, string Local) SplitToken(string part) => SplitAxisToken(part);
}
