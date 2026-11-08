#nullable enable

using System;
using System.Collections.Generic;

namespace Share.Conversation;

/// <summary>
/// 会话帧**事实字段**的单源（读路径事实声明 <c>Config\nodes\read-paths.json</c> 的 C# 消费面）。
/// <para>
/// 三态语义：字段缺失/不可解析 ⇒ <c>null</c>（未知），**禁**默认 <c>false</c> 冒充事实；
/// 表现层与渲染层共用本类字段名与读取原语（禁各处再写字面量）。
/// </para>
/// </summary>
public static class FrameFacts
{
    /// <summary>终答事实字段名。</summary>
    public const string TerminalField = "terminal";

    /// <summary>归档事实字段名。</summary>
    public const string ArchivedField = "archived";

    /// <summary>呈现类字段名（声明塑形产出）。</summary>
    public const string PresentationField = "presentation";

    /// <summary>角色字段名。</summary>
    public const string RoleField = "role";

    /// <summary>轮次字段名。</summary>
    public const string RoundField = "round";

    /// <summary>帧字段 → 三态事实（缺失/不可解析 ⇒ <c>null</c> = 未知）。单源 = <see cref="ConversationFacts.FlagOf"/>。</summary>
    /// <param name="fields">帧字段袋。</param>
    /// <param name="field">事实字段名。</param>
    public static bool? Read(IReadOnlyDictionary<string, string>? fields, string field)
        => ConversationFacts.FlagOf(fields, field);

    /// <summary>帧字段 → 文本（缺失 ⇒ <c>null</c>；不伪造空串）。单源 = <see cref="ConversationFacts.TextOf"/>。</summary>
    /// <param name="fields">帧字段袋。</param>
    /// <param name="field">字段名。</param>
    public static string? Text(IReadOnlyDictionary<string, string>? fields, string field)
        => ConversationFacts.TextOf(fields, field);

    /// <summary>帧字段 → 轮次（缺失/非法 ⇒ 0 = 会话级，不伪造第 1 轮）。</summary>
    /// <param name="fields">帧字段袋。</param>
    public static int Round(IReadOnlyDictionary<string, string>? fields)
        => int.TryParse(Text(fields, RoundField), out var round) ? round : 0;
}
