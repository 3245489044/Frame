using System;
using System.Collections.Generic;
using Share.Conversation;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 入史消息投影（**单源**，DUP-3 / DEBT-4 收口）—— 「标记节点 → 规范消息」的两件事
/// （角色选择 + 文本提取）唯一实现；代理投影器与轮末沉淀流共用，消除两处逐字复制。
/// <para>
/// 分层：本类只做**中立形状**（<see cref="ConversationMessage"/>），不做载荷 JSON 编码
/// （编码选项属应用层存储关切，见 Agent 侧 <c>RoundMessagePayload</c>）。
/// </para>
/// </summary>
public static class RoundMessageProjection
{
    /// <summary>标记 → 消息角色：终答 = <see cref="ConversationRole.Assistant"/>；其余（编号中间节点）= <see cref="ConversationRole.Tool"/>。</summary>
    /// <param name="mark">入史标记名（<see cref="NodeHistoryMark"/> 具名值；不可识别 ⇒ Tool）。</param>
    public static ConversationRole RoleOf(string? mark)
        => string.Equals(mark, nameof(NodeHistoryMark.Terminal), StringComparison.Ordinal)
            ? ConversationRole.Assistant
            : ConversationRole.Tool;

    /// <summary>节点产出的首个非空白端口文本（无端口/全空 → null）。</summary>
    /// <param name="ports">节点产出端口值。</param>
    public static string? FirstPortText(IReadOnlyDictionary<string, object?>? ports)
    {
        if (ports is null || ports.Count == 0) return null;
        foreach (var value in ports.Values)
        {
            if (value is null) continue;
            var text = value as string ?? value.ToString();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }

        return null;
    }

    /// <summary>标记节点 → 单条规范消息（终答 = assistant；编号中间节点 = tool 分层气泡）。</summary>
    /// <param name="mark">入史标记名。</param>
    /// <param name="nodeKey">节点键（消息 stage/subAgent 身份来源）。</param>
    /// <param name="text">消息文本（已由调用方完成终答/端口文本选择）。</param>
    /// <param name="round">轮次号。</param>
    /// <param name="atUtc">产出时刻（default ⇒ 当前时刻）。</param>
    public static ConversationMessage ToMessage(string? mark, string nodeKey, string text, int round, DateTimeOffset atUtc)
    {
        var isTerminal = string.Equals(mark, nameof(NodeHistoryMark.Terminal), StringComparison.Ordinal);
        return new ConversationMessage(
            isTerminal ? ConversationRole.Assistant : ConversationRole.Tool,
            nodeKey,
            isTerminal ? null : nodeKey,
            text,
            round,
            atUtc == default ? DateTimeOffset.UtcNow : atUtc);
    }
}
