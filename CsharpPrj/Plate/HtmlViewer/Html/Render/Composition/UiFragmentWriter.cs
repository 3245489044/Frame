#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Modules.Parsing.Markup;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 片段写出器（通用件，唯一节点装配点 + 唯一字符串出口）：
/// 模板 → 节点、片段根、流段包裹、文本占位、列表拼接，以及节点 → HTML 序列化。
/// <para>纪律：结构组合只产节点，不产 HTML 字符串；需要字符串时只经本类序列化
/// （多节点聚合用 <see cref="string.Join(string, IEnumerable{string})"/>，禁 StringBuilder/Concat 拼标签）。
/// 消费方（会话渲染、区域流水线、宿主页渲染）不再各自持有装配/序列化助手。</para>
/// </summary>
public static class UiFragmentWriter
{
    /// <summary>空片段。</summary>
    public static UiNode Empty { get; } = Fragment(Array.Empty<UiNode>());

    /// <summary>渲染命名模板为节点（缺失 ⇒ 回退节点）。</summary>
    public static IReadOnlyList<UiNode> Render(
        IReadOnlyDictionary<string, string> templates,
        string name,
        IReadOnlyDictionary<string, object?> model,
        IReadOnlyList<UiNode>? fallback = null)
        => HtmlTemplateEngine.RenderToNodes(templates, name, model, fallback);

    /// <summary>渲染命名模板为**受信片段**（框架内组合的唯一字符串出口包装）。</summary>
    public static UiMarkup RenderMarkup(
        IReadOnlyDictionary<string, string> templates,
        string name,
        IReadOnlyDictionary<string, object?> model,
        string fallback = "")
        => UiMarkup.Trusted(HtmlTemplateEngine.Render(templates, name, model, fallback));

    /// <summary>片段根（Tag 空串 + 子节点）：与页面 <c>appendInto</c> 同语义。</summary>
    public static UiNode Fragment(IReadOnlyList<UiNode> children)
        => new(string.Empty, Children: children);

    /// <summary>流段包裹（段根带稳定键）：<c>&lt;div class="stream-item" data-key="{key}"&gt;</c>。</summary>
    public static UiNode WrapStream(string key, IReadOnlyList<UiNode> children)
        => new("div", key, new Dictionary<string, string>(StringComparer.Ordinal)
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

    /// <summary>单节点序列化（null ⇒ 空串）。</summary>
    public static string Serialize(UiNode? node) => UiNodeHtml.ToHtml(node);

    /// <summary>多节点顺序拼接（唯一致富点；输入已是序列化产物）。</summary>
    public static string SerializeAll(IReadOnlyList<UiNode>? nodes)
        => nodes is { Count: > 0 } ? string.Join(string.Empty, nodes.Select(Serialize)) : string.Empty;
}
