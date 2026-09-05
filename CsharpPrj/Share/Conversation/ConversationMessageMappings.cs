using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Conversation;

/// <summary>
/// 会话消息的通用形态映射 —— Share 层的单一转换点（SingleImplementation），把 <see cref="ConversationMessage"/>
/// 映射到各消费方需要的异形（字典数组 / LLM 消息 / Cline content 解析），消灭 Agent 各层自造消息形态。
/// 依赖方向：本扩展仅依赖 System + <see cref="ConversationMessage"/>，零 Agent 依赖；LLM wire 形态的最终
/// 投影（role/content 对）由消费方（Agent.Contract.ConversationLlmProjector）在此之上做 1:1 映射。
/// </summary>
public static class ConversationMessageMappings
{
    /// <summary>消息 → 字典数组（{"role","content"} 键，供 Cline 式 / 字典型历史消费）；null 输入返回空列表。</summary>
    public static IReadOnlyList<Dictionary<string, object>> ToDictList(this IEnumerable<ConversationMessage>? messages)
    {
        if (messages is null) return Array.Empty<Dictionary<string, object>>();
        return messages
            .Select(m => new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["role"] = RoleName(m.Role),
                ["content"] = m.Text ?? string.Empty,
            })
            .ToList();
    }

    /// <summary>字典数组（{"role","content"}）→ 消息列表（Round 归 0 会话级，AtUtc=UtcNow）；null/空/非法条目跳过。</summary>
    public static List<ConversationMessage> FromDictList(object? rawHistory)
    {
        var list = new List<ConversationMessage>();
        if (rawHistory is not IEnumerable<object> entries) return list;
        foreach (var msg in entries)
        {
            if (msg is not Dictionary<string, object> dict ||
                !dict.TryGetValue("role", out var role) || !dict.TryGetValue("content", out var content))
                continue;
            list.Add(new ConversationMessage(
                ParseRole(role?.ToString()),
                null, null,
                content?.ToString() ?? string.Empty,
                0,
                DateTimeOffset.UtcNow));
        }
        return list;
    }

    /// <summary>角色枚举 → 传输/展示角色名（system/user/assistant/tool/error）。</summary>
    public static string RoleName(ConversationRole role) => role switch
    {
        ConversationRole.System => "system",
        ConversationRole.User => "user",
        ConversationRole.Assistant => "assistant",
        ConversationRole.Tool => "tool",
        ConversationRole.Error => "error",
        _ => "system",
    };

    /// <summary>角色名（大小写不敏感）→ 角色枚举；未知 → User（与历史既有宽容读取一致）。</summary>
    public static ConversationRole ParseRole(string? role) => string.Equals(role, "system", StringComparison.OrdinalIgnoreCase)
        ? ConversationRole.System
        : string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)
            ? ConversationRole.Assistant
            : string.Equals(role, "tool", StringComparison.OrdinalIgnoreCase)
                ? ConversationRole.Tool
                : string.Equals(role, "error", StringComparison.OrdinalIgnoreCase)
                    ? ConversationRole.Error
                    : ConversationRole.User;
}
