#nullable enable

using System.Collections.Generic;
using Share.Conversation.Rendering;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 带渲染上下文的节点渲染端口（桥内唯一渲染面）：
/// <see cref="IConversationRenderer"/> 仍是平台中立的 string 端口；本接口是**同一实现**的节点出口，
/// 下游按节点对齐写回（不再把 HTML 解析回树）。
/// </summary>
public interface IConversationFragmentRenderer : IConversationRenderer
{
    /// <summary>按稳定段键产节点片段（段键 = 流差分单位）。</summary>
    /// <param name="items">渲染项（源序）。</param>
    /// <param name="context">渲染上下文（可空 ⇒ 无上下文）。</param>
    IReadOnlyList<ConversationRenderFragment> RenderFragments(
        IReadOnlyList<ConversationRenderItem> items, ConversationRenderContext? context = null);
}

/// <summary>
/// 渲染上下文（**数据**，非 HTML）：宿主按次注入，使渲染器长期存活时不持有视图态。
/// <para><paramref name="RoundLabels"/> = 轮次头注记（轮 → 短标签）；<paramref name="ViewKey"/> = 视图声明键。</para>
/// </summary>
public sealed record ConversationRenderContext(
    IReadOnlyDictionary<int, string>? RoundLabels = null,
    string? ViewKey = null);
