using System;
using System.Collections.Generic;
using Operation.HierarchyMethod.Nodes.Execution;
using Share.Conversation;

namespace Operation.HierarchyMethod.History.Ports;

/// <summary>
/// 入史标记词表（框架声明，宿主注入）—— history 轴**唯一策略端口**：mark 具名值集 + 归一（薄壳委托
/// <see cref="NodeTags.Normalize(string?)"/>）+ mark→角色/类别派生（<see cref="RoleOfMark"/> / <see cref="KindOfMark"/>）。
/// <para>框架内容层不持任何宿主标记字面量；归一/派生经本词表，同源注入一次。</para>
/// </summary>
public sealed class HistoryMarkVocabulary
{
    private readonly string[] _marks;

    /// <summary>
    /// </summary>
    /// <param name="none">「不入史」mark 具名值（面板可见但不在历史视图）。</param>
    /// <param name="marks">全部具名值（含 <paramref name="none"/>）。</param>
    /// <param name="kindOfMark">mark → 内容类别（须 ∈ 宿主识别词表）。</param>
    /// <param name="roleOfMark">mark → 消息角色（缺省 = 终答 ⇒ Assistant / 其余 ⇒ Tool）；同一注入点给定。</param>
    public HistoryMarkVocabulary(
        string none,
        string[] marks,
        Func<string, string> kindOfMark,
        Func<string?, ConversationRole>? roleOfMark = null)
    {
        None = none ?? throw new ArgumentNullException(nameof(none));
        _marks = marks ?? throw new ArgumentNullException(nameof(marks));
        KindOfMark = kindOfMark ?? throw new ArgumentNullException(nameof(kindOfMark));
        RoleOfMark = roleOfMark ?? DefaultRoleOfMark;
    }

    /// <summary>「不入史」mark 具名值。</summary>
    public string None { get; }

    /// <summary>全部具名值（渲染/校验用）。</summary>
    public IReadOnlyList<string> Marks => _marks;

    /// <summary>mark → 内容类别映射（唯一映射点）。</summary>
    public Func<string, string> KindOfMark { get; }

    /// <summary>mark → 消息角色映射（唯一映射点）。</summary>
    public Func<string?, ConversationRole> RoleOfMark { get; }

    /// <summary>mark 具名 token 集归一（**薄壳委托** <see cref="NodeTags.Normalize(string?)"/>：
    /// 分隔符 <c>|</c>/<c>,</c>/<c>;</c>；空 = None；含未识别 token ⇒ null = 拒绝，不静默降级）。</summary>
    /// <param name="mark">原始标记（可为 token 集，如 <c>Terminal|Port</c>）。</param>
    public string? Normalize(string? mark) => NodeTags.Normalize(mark);

    private static ConversationRole DefaultRoleOfMark(string? mark)
        => NodeTags.IsTerminal(mark) ? ConversationRole.Assistant : ConversationRole.Tool;
}
