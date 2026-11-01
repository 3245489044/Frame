#nullable enable

using System;
using Modules.Node.Frame;
using Share.Conversation.Rendering;

namespace Share.Conversation.Presentation;

/// <summary>
/// **呈现身份唯一裁决点**（领域绑定；机制已下沉 <see cref="FramePresentation"/>）：
/// 把本地枚举投影为通道词/角色兜底类，再委托框架六步短路（顺序不得交换）。
/// <list type="number">
/// <item><c>terminal == true</c> ⇒ 声明的事实类（终答）</item>
/// <item><c>channel == Operation</c> ⇒ 声明的操作面类</item>
/// <item>帧字段 <c>presentation</c> 词 ∈ 声明类清单 ⇒ 该类（声明投影）</item>
/// <item><c>channel == Process</c> ⇒ 声明的过程面类</item>
/// <item>角色兜底表命中 ⇒ 该类（旧数据）</item>
/// <item>否则 ⇒ <see cref="PresentationDeclaration.UnknownId"/>（可见占位）</item>
/// </list>
/// </summary>
public static class PresentationResolver
{
    private const string OperationChannelWord = "operation";
    private const string ProcessChannelWord = "process";

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

        var fallback = declaration.RoleFallback.TryGetValue(role, out var value) ? value : null;
        var (classId, origin) = FramePresentation.Resolve(
            declaration.Classes.Keys,
            declaration.TerminalClass,
            ChannelWord(channel),
            OperationChannelWord,
            declaration.OperationChannelClass,
            ProcessChannelWord,
            declaration.ProcessChannelClass,
            fallback,
            presentation,
            terminal);

        return (classId, ToOrigin(origin));
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

    private static string ChannelWord(MessageChannel channel)
        => channel switch
        {
            MessageChannel.Operation => OperationChannelWord,
            MessageChannel.Process => ProcessChannelWord,
            _ => "dialogue",
        };

    private static PresentationOrigin ToOrigin(string origin)
        => origin switch
        {
            FramePresentation.OriginFact => PresentationOrigin.Fact,
            FramePresentation.OriginDeclared => PresentationOrigin.Declared,
            FramePresentation.OriginLegacy => PresentationOrigin.Legacy,
            _ => PresentationOrigin.Unknown,
        };
}
