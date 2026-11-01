using System;
using System.Collections.Generic;
using Share.Conversation.ContextBinding;

namespace Share.Conversation;

/// <summary>
/// 单条会话消息。
/// </summary>
/// <param name="Role">消息角色（<see cref="ConversationRole"/>）。</param>
/// <param name="Stage">会话工作流步骤标识（声明式：如 "cline_execute"/"answer"…，不限定层数/子智能体）；null = 会话级/user/assistant。</param>
/// <param name="SubAgent">产生方执行器函数名（"cline_execute"/"answer"/…）；null = user/assistant。</param>
/// <param name="Text">消息文本。</param>
/// <param name="Round">会话累计轮次（0 = 会话级/system；user 自 1 起单调）。</param>
/// <param name="AtUtc">产生时刻（UTC）。</param>
/// <param name="Payload">外部消息原文载荷（可选；如 Cline 对话映射时保留 <c>ClineMessage</c> 原文 JSON，供 resume 精确重建；默认 null）。</param>
/// <param name="Binding">上下文绑定（可选；锚点键/来源域/投喂目标/文本哈希，供 UI≡LLM 一致性校验；默认 null）。尾参，不影响既有位置构造调用。</param>
/// <param name="Channel">呈现通道（默认 <see cref="MessageChannel.Dialogue"/> = 对话气泡；<see cref="MessageChannel.Operation"/> = 操作流）。
/// 与 <see cref="Binding"/> 的 FeedTarget（投喂目标）正交；尾参，不影响既有位置构造调用。</param>
/// <param name="Targets">本轮分发/注入的**目标集**（dispatch 执行体产出的事实；F3）。null/空 = 无分发（reply/status）。
/// 尾参，不影响既有位置构造调用。</param>
/// <param name="Terminal">写侧终答事实（<c>terminal</c> 帧字段）：true = 本轮终答；false = 非终答；
/// null = 未知（旧数据/未落库，不冒充 false）。尾参，不影响既有位置构造调用。</param>
/// <param name="Archived">写侧归档事实（<c>archived</c> 帧字段）：true = 已入史归档；false = 未归档；
/// null = 未知。尾参，不影响既有位置构造调用。</param>
/// <param name="Presentation">写侧呈现词（<c>presentation</c> 帧字段/声明投影；<see cref="Presentation.PresentationWords"/> 受控词）。
/// null = 未声明（读侧走事实/旧数据分支）。尾参，不影响既有位置构造调用。</param>
public sealed record ConversationMessage(
    ConversationRole Role,
    string? Stage,
    string? SubAgent,
    string Text,
    int Round,
    DateTimeOffset AtUtc,
    string? Payload = null,
    MessageBinding? Binding = null,
    MessageChannel Channel = MessageChannel.Dialogue,
    IReadOnlyList<string>? Targets = null,
    bool? Terminal = null,
    bool? Archived = null,
    string? Presentation = null);
