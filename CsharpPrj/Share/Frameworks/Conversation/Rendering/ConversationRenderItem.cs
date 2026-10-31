using System.Collections.Generic;
using Share.Conversation;

namespace Share.Conversation.Rendering;

/// <summary>
/// 单条会话消息的中立渲染项 —— 与存储记录（<see cref="ConversationMessage"/>）解耦的展示数据投影，
/// 供 <see cref="IConversationRenderer"/> 消费。不改持久化模型（消息记录仍全量序列化），
/// 仅承载"如何呈现"所需字段，使渲染器不依赖具体记录类型（IoC 反转、载体无关）。
/// </summary>
/// <param name="Terminal">终答事实（写侧落库 <c>terminal</c> 帧字段；null = 未知，禁默认 false）。</param>
/// <param name="Archived">归档事实（写侧落库 <c>archived</c> 帧字段；null = 未知）。</param>
public sealed record ConversationRenderItem(
    ConversationRole Role,
    string? Stage,
    string? SubAgent,
    string Text,
    int Round,
    MessageChannel Channel = MessageChannel.Dialogue,
    IReadOnlyList<string>? Targets = null,
    bool? Terminal = null,
    bool? Archived = null);

