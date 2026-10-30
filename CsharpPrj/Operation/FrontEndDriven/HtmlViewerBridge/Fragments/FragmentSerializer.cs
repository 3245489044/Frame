#nullable enable

using System.Collections.Generic;
using System.Linq;
using UiBackend.Abstractions;
using UiBackend.Html;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 节点 → HTML 的**唯一序列化出口**（薄转发 plate 的 <see cref="UiNodeHtml.ToHtml"/>）。
/// <para>纪律：桥内任何渲染代码都不得自行拼接 HTML 串；需要字符串时只能经本类。
/// 多节点聚合用 <see cref="string.Join(string, IEnumerable{string})"/>（禁 StringBuilder/Concat 拼标签）。</para>
/// </summary>
public static class FragmentSerializer
{
    /// <summary>单节点序列化（null ⇒ 空串）。</summary>
    public static string Serialize(UiNode? node) => UiNodeHtml.ToHtml(node);

    /// <summary>多节点顺序拼接（唯一致富点；输入已是序列化产物）。</summary>
    public static string SerializeAll(IReadOnlyList<UiNode>? nodes)
        => nodes is { Count: > 0 } ? string.Join(string.Empty, nodes.Select(Serialize)) : string.Empty;
}
