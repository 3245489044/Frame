using Share.Conversation;

namespace Share.Conversation.Rendering;

/// <summary>
/// 单条会话消息的中立渲染项 —— 与存储记录（<see cref="ConversationMessage"/>）解耦的展示数据投影，
/// 供 <see cref="IConversationRenderer"/> 消费。不改持久化模型（消息记录仍全量序列化），
/// 仅承载"如何呈现"所需字段，使渲染器不依赖具体记录类型（IoC 反转、载体无关）。
/// </summary>
public sealed record ConversationRenderItem(
    ConversationRole Role,
    string? Stage,
    string? SubAgent,
    string Text,
    int Round);
