using System.Collections.Generic;

namespace Share.Conversation.Adapters;

/// <summary>
/// 智能体 wire 适配端口（IoC 反转）—— 把规范对话结构（<see cref="ConversationMessage"/> 序列）渲染为
/// 传统 JSON 对话智能体（Cline / Claude / OpenAI 兼容）所消费的标准对话 JSON。
/// 本端口定义于 Share（载体无关、零 Agent 依赖）；**具体 wire 实现由 Agent 框架注入注册**（控制反转），
/// 使「节点 → 标准对话 → 任意智能体」的适配链可扩展、不硬编码于 Share。
/// </summary>
public interface IConversationAgentWire
{
    /// <summary>智能体标识（cline / claude / openai）。</summary>
    string Name { get; }

    /// <summary>把规范对话结构渲染为该智能体的 wire JSON。</summary>
    string Render(IReadOnlyList<ConversationMessage> messages);
}

