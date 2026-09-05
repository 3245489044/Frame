namespace Share.Conversation;

/// <summary>
/// 会话消息角色 —— 标识一条对话消息的产生方与展示语义。
/// <see cref="System"/> = 会话级/系统提示；<see cref="User"/> = 用户/外部智能体发消息；
/// <see cref="Assistant"/> = 链终点终答；<see cref="Tool"/> = 工作流中层子智能体产出（气泡可折叠）；
/// <see cref="Error"/> = 失败/诊断。
/// </summary>
public enum ConversationRole
{
    System,
    User,
    Assistant,
    Tool,
    Error,
}
