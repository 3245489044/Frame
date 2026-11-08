namespace Share.Conversation;

/// <summary>
/// 角色词形态（wire 名 / 结构样式 token / 展示标签）—— 同一角色的三种表示**同源产出**。
/// <para>
/// 单源理由：此前 wire 名（<see cref="ConversationMessageMappings.RoleName"/>）、样式 token
/// （<see cref="Rendering.ConversationRolePresentation.ClassOf"/>）、展示标签分三处 switch，
/// 同一 <see cref="ConversationRole.Tool"/> 曾同时得到 <c>tool</c> 与 <c>proc</c>。
/// </para>
/// </summary>
/// <param name="Wire">传输/展示角色名（system/user/assistant/tool/error）。</param>
/// <param name="Class">结构样式 token（user/assistant/proc/system/error）。</param>
/// <param name="Label">默认展示标签（中文；上层可覆盖）。</param>
public readonly record struct ConversationRoleWording(string Wire, string Class, string Label);

/// <summary>角色词映射（**唯一 switch**）：wire 名 / 样式 token / 标签一律经本类派生。</summary>
public static class ConversationRoleWordingMap
{
    /// <summary>角色 → 三形态词。</summary>
    /// <param name="role">会话角色。</param>
    public static ConversationRoleWording Of(ConversationRole role) => role switch
    {
        ConversationRole.User => new ConversationRoleWording("user", "user", "用户"),
        ConversationRole.Assistant => new ConversationRoleWording("assistant", "assistant", "助手"),
        ConversationRole.Tool => new ConversationRoleWording("tool", "proc", "工具"),
        ConversationRole.Error => new ConversationRoleWording("error", "error", "错误"),
        _ => new ConversationRoleWording("system", "system", "系统"),
    };

    /// <summary>角色名（大小写不敏感）→ 角色枚举；未知 → User（与历史既有宽容读取一致）。</summary>
    /// <param name="role">角色名（可空）。</param>
    public static ConversationRole Parse(string? role)
        => string.Equals(role, "system", StringComparison.OrdinalIgnoreCase) ? ConversationRole.System
            : string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase) ? ConversationRole.Assistant
            : string.Equals(role, "tool", StringComparison.OrdinalIgnoreCase) ? ConversationRole.Tool
            : string.Equals(role, "error", StringComparison.OrdinalIgnoreCase) ? ConversationRole.Error
            : ConversationRole.User;
}
