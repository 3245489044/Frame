#nullable enable

using System.Collections.Generic;
using UiBackend.Abstractions;
using UiBackend.Html;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 节点片段助手（桥内**唯一**节点装配点）：模板 → 节点、片段根、流段包裹、文本占位。
/// <para>纪律：本类只做结构组合（元素/文本/子节点），不产出 HTML 字符串；字符串出口唯一 =
/// <see cref="FragmentSerializer"/>。</para>
/// </summary>
public static class FragmentTemplates
{
    /// <summary>渲染命名模板为节点（缺失 ⇒ 回退节点）。</summary>
    public static IReadOnlyList<UiNode> Render(
        IReadOnlyDictionary<string, string> templates,
        string name,
        IReadOnlyDictionary<string, object?> model,
        IReadOnlyList<UiNode>? fallback = null)
        => HtmlTemplateEngine.RenderToNodes(templates, name, model, fallback);

    /// <summary>片段根（Tag 空串 + 子节点）：与页面 <c>appendInto</c> 同语义。</summary>
    public static UiNode Fragment(IReadOnlyList<UiNode> children)
        => new(string.Empty, Children: children);

    /// <summary>空片段。</summary>
    public static UiNode Empty { get; } = Fragment(System.Array.Empty<UiNode>());

    /// <summary>流段包裹（段根带稳定键）：<c>&lt;div class="stream-item" data-key="{key}"&gt;</c>。</summary>
    public static UiNode WrapStream(string key, IReadOnlyList<UiNode> children)
        => new("div", key, new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            ["class"] = "stream-item",
            ["data-key"] = key,
        }, null, children);

    /// <summary>文本节点（空串 ⇒ 不产节点）。</summary>
    public static IReadOnlyList<UiNode> Texts(params string?[] values)
    {
        var nodes = new List<UiNode>(values.Length);
        foreach (var value in values)
        {
            if (!string.IsNullOrEmpty(value)) nodes.Add(UiNode.TextNode(value));
        }

        return nodes;
    }

    /// <summary>列表拼接（节点级，零字符串）。</summary>
    public static IReadOnlyList<UiNode> Concat(params IReadOnlyList<UiNode>?[] parts)
    {
        var nodes = new List<UiNode>();
        foreach (var part in parts)
        {
            if (part is { Count: > 0 }) nodes.AddRange(part);
        }

        return nodes;
    }

    /// <summary>单节点 → 片段（便于嵌套片段根）。</summary>
    public static IReadOnlyList<UiNode> One(UiNode node) => new[] { node };
}
