#nullable enable

using Share.Conversation;
using Share.Conversation.Rendering;

namespace Share.Conversation.Presentation;

/// <summary>
/// **呈现身份唯一裁决点**（纯函数，表驱动短路）：事实 → 声明投影 → 旧数据兜底 → 未知。
/// <para>
/// 判据全部来自 <see cref="PresentationDeclaration"/>（产品声明面）；框架不持有任何具体词。
/// 判定顺序（自上而下短路，不得交换）：
/// </para>
/// <list type="number">
/// <item><c>terminal == true</c> ⇒ 声明的事实类（终答）</item>
/// <item><c>channel == Operation</c> ⇒ 声明的操作面类</item>
/// <item>帧字段 <c>presentation</c> 词 ∈ 声明类清单 ⇒ 该类（声明投影）</item>
/// <item><c>channel == Process</c> ⇒ 声明的过程面类</item>
/// <item>角色兜底表命中 ⇒ 该类（旧数据）</item>
/// <item>否则 ⇒ <see cref="PresentationDeclaration.UnknownId"/>（可见占位）</item>
/// </list>
/// <para><c>Stage</c> 不参与判定：参与即等于把已删除的 role:kind 锚点规则换名保留。</para>
/// </summary>
public static class PresentationResolver
{
    /// <summary>呈现身份 + 来源。</summary>
    /// <param name="declaration">呈现声明（产品策略；缺失用 <see cref="PresentationDeclaration.Empty"/>）。</param>
    /// <param name="role">产生方角色。</param>
    /// <param name="channel">呈现通道。</param>
    /// <param name="presentation">呈现词（帧字段；null = 未声明）。</param>
    /// <param name="terminal">终答事实（null = 未知，禁默认 false）。</param>
    public static (string ClassId, PresentationOrigin Origin) Resolve(
        PresentationDeclaration declaration,
        ConversationRole role,
        MessageChannel channel,
        string? presentation,
        bool? terminal)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        // P1 写侧事实：终答。
        if (terminal == true && declaration.TerminalClass.Length > 0)
        {
            return (declaration.TerminalClass, PresentationOrigin.Fact);
        }

        // P2 写侧事实：操作面。
        if (channel == MessageChannel.Operation && declaration.OperationChannelClass.Length > 0)
        {
            return (declaration.OperationChannelClass, PresentationOrigin.Fact);
        }

        // P3 声明投影（词必须已在声明类清单中登记）。
        if (!string.IsNullOrWhiteSpace(presentation))
        {
            var word = presentation.Trim().ToLowerInvariant();
            if (declaration.Classes.ContainsKey(word))
            {
                return (word, PresentationOrigin.Declared);
            }
        }

        // P4 过程通道（旧数据）：不得因角色被提升为气泡。
        if (channel == MessageChannel.Process && declaration.ProcessChannelClass.Length > 0)
        {
            return (declaration.ProcessChannelClass, PresentationOrigin.Fact);
        }

        // P5 旧数据兜底（角色 → 声明类）。
        if (declaration.RoleFallback.TryGetValue(role, out var fallback))
        {
            return (fallback, PresentationOrigin.Legacy);
        }

        // P6 未知：可见，不猜。
        return (PresentationDeclaration.UnknownId, PresentationOrigin.Unknown);
    }

    /// <summary>消息 → 呈现身份（单一入口；渲染项与合并逻辑共用）。</summary>
    /// <param name="declaration">呈现声明。</param>
    /// <param name="message">规范会话消息。</param>
    public static (string ClassId, PresentationOrigin Origin) Resolve(
        PresentationDeclaration declaration, ConversationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Resolve(declaration, message.Role, message.Channel, message.Presentation, message.Terminal);
    }

    /// <summary>渲染项 → 呈现身份（与消息同判据）。</summary>
    /// <param name="declaration">呈现声明。</param>
    /// <param name="item">渲染项。</param>
    public static (string ClassId, PresentationOrigin Origin) Resolve(
        PresentationDeclaration declaration, ConversationRenderItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Resolve(declaration, item.Role, item.Channel, item.Presentation, item.Terminal);
    }
}

