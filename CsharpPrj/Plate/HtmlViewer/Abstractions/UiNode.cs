#nullable enable

using System.Collections.Generic;
using Modules.Parsing.Markup;

namespace UiBackend.Abstractions;

/// <summary>
/// 键控节点树（L1 通用契约）——渲染产物的中立形态（替代 HTML 串作写输入）：
/// 节点 = 标签 + 稳定键 + 属性 + 文本 + 子节点 +（可选）受控态。后端无关（HTML/未来后端各自映射到写原语）。
/// </summary>
public sealed record UiNode(
    string Tag,
    string? Key = null,
    IReadOnlyDictionary<string, string>? Attributes = null,
    string? Text = null,
    IReadOnlyList<UiNode>? Children = null,
    UiControlState? Controlled = null)
{
    /// <summary>受信原文标记（公开只读；**仅** <see cref="RawText"/> 可置位，无位置构造旁路）。</summary>
    public bool Raw { get; private init; }

    /// <summary>文本节点（<see cref="Tag"/> 为空串，仅 <see cref="Text"/> 有值）。</summary>
    public static UiNode TextNode(string text) => new(string.Empty, Text: text);

    /// <summary>**受信原文**文本节点（模板字面量 HTML；不做二次转义）。仅框架内使用：外部入口唯一 = <see cref="Markup"/>。</summary>
    internal static UiNode RawText(string html) => new(string.Empty, Text: html) { Raw = true };

    /// <summary>**受信片段**节点（类型化入口；替代裸字符串，防把未编码文本误当片段）。</summary>
    public static UiNode Markup(UiMarkup markup) => RawText(markup.Html);

    /// <summary>元素节点（便捷构造）。</summary>
    public static UiNode Element(
        string tag,
        string? key = null,
        IReadOnlyDictionary<string, string>? attributes = null,
        IReadOnlyList<UiNode>? children = null,
        UiControlState? controlled = null)
        => new(tag, key, attributes, null, children, controlled);

    /// <summary>是否文本节点。</summary>
    public bool IsText => Tag.Length == 0;
}
