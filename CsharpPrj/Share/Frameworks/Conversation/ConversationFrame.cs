using System;
using System.Collections.Generic;
using Share.Conversation.ContextBinding;
using Share.Conversation.Rendering;

namespace Share.Conversation;

/// <summary>
/// 会话帧（**唯一帧事实载体**）：Kind 与 Stage 分离，事实字段袋必填。
/// <para>
/// 设计约束：
/// <list type="bullet">
/// <item><see cref="Kind"/> = 帧语义类别（<c>frame-kinds.json</c> 主键），呈现/字段契约只按它查；</item>
/// <item><see cref="Stage"/> = 工作流阶段（可选；不再是 kind 的替身）；</item>
/// <item><see cref="Facts"/> = 写侧事实字段袋（禁空引用；无事实须显式 <see cref="ConversationFacts.Empty"/>）；</item>
/// <item>消息/渲染项只经 <see cref="ToMessage"/> / <see cref="ToRenderItem"/> 投影，禁各自 <c>new</c>。</item>
/// </list>
/// </para>
/// </summary>
/// <param name="Role">会话角色。</param>
/// <param name="Kind">帧类别（词表主键）。</param>
/// <param name="Text">正文。</param>
/// <param name="Round">轮次（0 = 会话级）。</param>
/// <param name="Facts">事实字段袋（必填；无事实 = Empty）。</param>
/// <param name="Seq">域内到达序（稳定排序用；0 = 未标）。</param>
/// <param name="Stage">工作流阶段（可空；与 Kind 正交）。</param>
/// <param name="SubAgent">产出执行体（可空）。</param>
/// <param name="Channel">呈现通道（可空 = 未声明）。</param>
/// <param name="Targets">分发目标集（可空 = 无分发）。</param>
/// <param name="Terminal">终答事实（null = 读字段袋/未知）。</param>
/// <param name="Archived">归档事实（null = 读字段袋/未知）。</param>
/// <param name="Presentation">呈现词（null = 读字段袋/未声明）。</param>
/// <param name="Binding">锚点绑定（可空）。</param>
/// <param name="Origin">事实来源（Live/Snapshot/Derived；可空）。</param>
/// <param name="AtUtc">产生时刻（default = 投影时取当前时刻）。</param>
/// <param name="Payload">外部消息原文载荷（可选；无损回环用）。</param>
public sealed record ConversationFrame(
    ConversationRole Role,
    string Kind,
    string Text,
    int Round,
    ConversationFacts Facts,
    long Seq = 0,
    string? Stage = null,
    string? SubAgent = null,
    MessageChannel? Channel = null,
    IReadOnlyList<string>? Targets = null,
    bool? Terminal = null,
    bool? Archived = null,
    string? Presentation = null,
    MessageBinding? Binding = null,
    string? Origin = null,
    DateTimeOffset AtUtc = default,
    string? Payload = null)
{
    /// <summary>生效终答事实（**只认显式值**：过程帧传 null 即未知，禁从字段袋补出 false/true）。</summary>
    public bool? EffectiveTerminal => Terminal;

    /// <summary>生效归档事实（只认显式值；缺失 = 未知）。</summary>
    public bool? EffectiveArchived => Archived;

    /// <summary>生效呈现词（显式值优先，缺省读字段袋）。</summary>
    public string? EffectivePresentation => Presentation ?? Facts.Text("presentation");

    /// <summary>投影为规范消息（**唯一 <c>new ConversationMessage</c> 点**）。</summary>
    public ConversationMessage ToMessage()
    {
        // kind 单源：帧类别显式落袋，读侧（渲染/去重）不再以 Stage 猜类别。
        var facts = string.IsNullOrWhiteSpace(Kind) || Facts.Text("kind") is not null
            ? Facts
            : Facts.With("kind", Kind);
        return new(
            Role,
            Stage,
            SubAgent,
            Text,
            Round,
            AtUtc == default ? DateTimeOffset.UtcNow : AtUtc,
            Payload: Payload,
            Binding: Binding,
            Channel: Channel,
            Targets: Targets,
            Terminal: Terminal,
            Archived: Archived,
            Presentation: EffectivePresentation,
            Fields: facts.Values,
            Kind: string.IsNullOrWhiteSpace(Kind) ? facts.Text("kind") : Kind);
    }

    /// <summary>投影为渲染项（UI 面；过渡期使用，目标由 ViewRow 直接承载帧）。</summary>
    public ConversationRenderItem ToRenderItem()
        => new(
            Role,
            Stage,
            SubAgent,
            Text,
            Round,
            Channel,
            Targets,
            Terminal,
            Archived,
            EffectivePresentation,
            Facts.Values,
            Kind);

    /// <summary>既有消息 → 帧（迁移/兼容入口；字段袋原样透传）。</summary>
    public static ConversationFrame FromMessage(ConversationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var facts = ConversationFacts.From(message.Fields);
        return new ConversationFrame(
            message.Role,
            !string.IsNullOrWhiteSpace(message.Kind)
                ? message.Kind!
                : facts.Text("kind") ?? string.Empty,
            message.Text,
            message.Round,
            facts,
            Stage: message.Stage,
            SubAgent: message.SubAgent,
            Channel: message.Channel,
            Targets: message.Targets,
            Terminal: message.Terminal ?? facts.Flag("terminal"),
            Archived: message.Archived ?? facts.Flag("archived"),
            Presentation: message.Presentation,
            Binding: message.Binding,
            AtUtc: message.AtUtc,
            Payload: message.Payload);
    }
}
