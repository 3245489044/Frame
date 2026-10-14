#nullable enable

using System.Collections.Generic;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 受控态绑定器（L5 态外置，L3 投影后端）——把页面回流的受控态（展开集合，按稳定键）绑定到键控节点树：
/// 命中的 <c>&lt;details&gt;</c> 补 <c>open</c> 属性，使渲染产物「态即数据」（<c>f(data,state)</c>），
/// 键控对齐时受控态随目标写入、未变节点保持。纯结构变换（零领域词汇）。
/// </summary>
public static class UiStateBinder
{
    /// <summary>把展开集合（稳定键集合，格式 <c>attr=value</c>）绑定到树（缺失/空 → 原样返回）。</summary>
    public static UiNode Bind(UiNode root, IReadOnlyCollection<string>? openKeys)
    {
        if (openKeys is null || openKeys.Count == 0 || root is null) return root;
        var set = openKeys as ISet<string> ?? new HashSet<string>(openKeys);
        return BindNode(root, set);
    }

    private static UiNode BindNode(UiNode node, ISet<string> openKeys)
    {
        var children = node.Children;
        if (children is { Count: > 0 })
        {
            List<UiNode>? mapped = null;
            for (var i = 0; i < children.Count; i++)
            {
                var bound = BindNode(children[i], openKeys);
                if (!ReferenceEquals(bound, children[i]) && mapped is null)
                {
                    mapped = new List<UiNode>(children.Count);
                    for (var j = 0; j < i; j++) mapped.Add(children[j]);
                }
                mapped?.Add(bound);
            }
            if (mapped is not null) node = node with { Children = mapped };
        }

        if (node.Tag.Length > 0
            && string.Equals(node.Tag, "details", System.StringComparison.OrdinalIgnoreCase)
            && node.Key is not null
            && openKeys.Contains(node.Key)
            && (node.Attributes is null || !node.Attributes.ContainsKey("open")))
        {
            var attrs = node.Attributes is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(node.Attributes);
            attrs["open"] = string.Empty;
            node = node with { Attributes = attrs, Controlled = new UiControlState(Open: true) };
        }
        return node;
    }
}
