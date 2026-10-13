#nullable enable

using System.Collections.Generic;

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
    /// <summary>文本节点（<see cref="Tag"/> 为空串，仅 <see cref="Text"/> 有值）。</summary>
    public static UiNode TextNode(string text) => new(string.Empty, Text: text);

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
