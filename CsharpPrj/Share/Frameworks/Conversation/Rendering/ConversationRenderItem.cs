using System.Collections.Generic;
using Share.Conversation;
using Share.Conversation.Presentation;

namespace Share.Conversation.Rendering;

/// <summary>
/// 单条会话消息的中立渲染项 —— 与存储记录（<see cref="ConversationMessage"/>）解耦的展示数据投影，
/// 供 <see cref="IConversationRenderer"/> 消费。不改持久化模型（消息记录仍全量序列化），
/// 仅承载"如何呈现"所需字段，使渲染器不依赖具体记录类型（IoC 反转、载体无关）。
/// </summary>
/// <param name="Terminal">终答事实（写侧落库 <c>terminal</c> 帧字段；null = 未知，禁默认 false）。</param>
/// <param name="Archived">归档事实（写侧落库 <c>archived</c> 帧字段；null = 未知）。</param>
/// <param name="Presentation">呈现词（声明投影/帧字段；null = 未声明，读侧走事实/旧数据分支）。</param>
/// <param name="Kind">帧类别（<c>frame-kinds.json</c> 主键）；null = 由字段袋 <c>kind</c> / 旧 Stage 兜底。</param>
public sealed record ConversationRenderItem(
    ConversationRole Role,
    string? Stage,
    string? SubAgent,
    string Text,
    int Round,
    MessageChannel? Channel = null,
    IReadOnlyList<string>? Targets = null,
    bool? Terminal = null,
    bool? Archived = null,
    string? Presentation = null,
    IReadOnlyDictionary<string, string>? Fields = null,
    string? Kind = null)
{
    /// <summary>
    /// 生效帧类别（**唯一口径**）：显式 Kind → 字段袋 <c>kind</c> → 旧消息 Stage（迁移兼容）。
    /// 呈现/字段契约只按本值查表，禁消费方直接读 <see cref="Stage"/> 当 kind。
    /// </summary>
    public string EffectiveKind
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Kind)) return Kind!;
            if (Fields is not null
                && Fields.TryGetValue("kind", out var fromFields)
                && !string.IsNullOrWhiteSpace(fromFields))
            {
                return fromFields;
            }

            return Stage ?? string.Empty;
        }
    }
}


