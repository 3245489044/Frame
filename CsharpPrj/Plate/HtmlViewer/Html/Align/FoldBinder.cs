#nullable enable

using System;
using System.Collections.Generic;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 折叠态绑定器（**单一协议**）——把用户折叠态/默认折叠态写到元素 &lt;details&gt; 上：
/// 命中 <c>data-fold-key</c> 的 details 一律写 <c>data-open-state</c>（"1"/"0"）并同步 <c>open</c> 属性；
/// 未命中 fold key 的节点不受影响。
/// <para>态来源优先级：用户态（<see cref="ParseState"/>） &gt; 模板默认态（<c>data-default-open</c>） &gt; 收起。</para>
/// <para>纯结构变换（零领域词汇）。region 与流段共用本绑定器（禁 stream 直接序列化未绑定节点）。</para>
/// </summary>
public static class FoldBinder
{
    /// <summary>折叠键属性（对齐 <c>data-key/data-name</c> 与折叠键解耦；捕获/绑定共用本名）。</summary>
    public const string FoldKeyAttribute = "data-fold-key";

    /// <summary>模板默认展开属性（仅初始值，不能覆盖用户态）。</summary>
    public const string DefaultOpenAttribute = "data-default-open";

    /// <summary>受控标记属性名（与 <see cref="UiStateBinder.OpenStateAttribute"/> 同源）。</summary>
    public const string OpenStateAttribute = UiStateBinder.OpenStateAttribute;

    /// <summary>线格式前缀（<c>uiState.open</c> 数组内携带全量折叠态，避免与对齐键集合混淆）。</summary>
    public const string StateEntryPrefix = "fold:";

    /// <summary>把 <c>uiState.open</c> 线条目解析为折叠态（<c>fold:key=1|0</c>；兼容 <c>data-fold-key=key</c>）。</summary>
    /// <param name="entries">交互态条目（null = 未携带）。</param>
    public static IReadOnlyDictionary<string, bool> ParseState(IReadOnlyList<string>? entries)
    {
        var state = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (entries is null) return state;

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            if (entry.StartsWith(StateEntryPrefix, StringComparison.Ordinal))
            {
                var body = entry.Substring(StateEntryPrefix.Length);
                var eq = body.LastIndexOf('=');
                if (eq <= 0 || eq == body.Length - 1) continue;
                state[body.Substring(0, eq)] = body.Substring(eq + 1) == "1";
            }
            else if (entry.StartsWith(FoldKeyAttribute + "=", StringComparison.Ordinal))
            {
                // 兼容旧捕获：仅出现展开项 ⇒ 视为展开。
                state[entry.Substring(FoldKeyAttribute.Length + 1)] = true;
            }
        }

        return state;
    }

    /// <summary>绑定折叠态（递归元素树；仅处理带 fold key 的 details）。</summary>
    /// <param name="root">渲染节点树。</param>
    /// <param name="state">用户折叠态（null = 无用户态，全部走模板默认）。</param>
    public static UiNode Bind(UiNode root, IReadOnlyDictionary<string, bool>? state)
    {
        if (root is null) return root;

        var children = root.Children;
        if (children is { Count: > 0 })
        {
            List<UiNode>? mapped = null;
            for (var i = 0; i < children.Count; i++)
            {
                var bound = Bind(children[i], state);
                if (!ReferenceEquals(bound, children[i]) && mapped is null)
                {
                    mapped = new List<UiNode>(children.Count);
                    for (var j = 0; j < i; j++) mapped.Add(children[j]);
                }

                mapped?.Add(bound);
            }

            if (mapped is not null) root = root with { Children = mapped };
        }

        if (root.Tag.Length == 0 || !string.Equals(root.Tag, "details", StringComparison.OrdinalIgnoreCase))
            return root;
        if (root.Attributes is not { } source || !source.TryGetValue(FoldKeyAttribute, out var foldKey) ||
            string.IsNullOrWhiteSpace(foldKey))
            return root;

        var open = state is not null && state.TryGetValue(foldKey, out var userOpen)
            ? userOpen
            : source.TryGetValue(DefaultOpenAttribute, out var fallback) && fallback == "1";

        var attrs = new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase)
        {
            [OpenStateAttribute] = open ? "1" : "0",
        };
        if (open) attrs["open"] = string.Empty;
        else attrs.Remove("open");

        return root with { Attributes = attrs, Controlled = new UiControlState(Open: open) };
    }
}
