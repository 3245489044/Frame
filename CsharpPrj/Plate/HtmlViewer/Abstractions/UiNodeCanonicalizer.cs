#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UiBackend.Abstractions;

/// <summary>
/// 节点树规范化（通用件，唯一实现）—— 把「键控树/文本/载荷」规范化为稳定可比较串：
/// 属性有序、子节点递归、段间不可歧义分隔。纯函数、零领域类型；哈希由上层注入（本层只管 canonical 形态）。
/// </summary>
public static class UiNodeCanonicalizer
{
    /// <summary>单载荷规范化（null / 文本 / 键控树 / 其它 ToString）。</summary>
    public static string Canonical(object? payload) => payload switch
    {
        null => string.Empty,
        string text => text,
        UiNode node => CanonicalNode(node),
        _ => payload.ToString() ?? string.Empty,
    };

    /// <summary>键控树规范化（属性有序；子节点递归）。</summary>
    public static string CanonicalNode(UiNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var sb = new StringBuilder(128);
        sb.Append('<').Append(node.Tag).Append(':').Append(node.Key ?? string.Empty);
        if (node.Attributes is { Count: > 0 })
        {
            foreach (var kv in node.Attributes.OrderBy(k => k.Key, StringComparer.Ordinal))
                sb.Append('|').Append(kv.Key).Append('=').Append(kv.Value);
        }

        if (!string.IsNullOrEmpty(node.Text)) sb.Append('>').Append(node.Text);
        if (node.Children is { Count: > 0 })
        {
            foreach (var child in node.Children) sb.Append('{').Append(CanonicalNode(child)).Append('}');
        }

        sb.Append("</>");
        return sb.ToString();
    }

    /// <summary>多段规范化拼接（保持调用方给定顺序；每段以不可歧义分隔符结束）。</summary>
    public static string Join(IEnumerable<string?> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var sb = new StringBuilder(256);
        foreach (var part in parts) sb.Append(part ?? string.Empty).Append('\u0001');
        return sb.ToString();
    }

    /// <summary>
    /// 渲染产物规范化 + 哈希（通用件，唯一实现）：文本 / 写计划 / 键控目标形状在此定型；
    /// 哈希实现由调用方注入（Plate 不持有哈希口径，避免反向依赖）。
    /// </summary>
    public static string Of(RenderOutput output, Func<string, string> hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        if (output is null || output.IsSkip) return hash("skip");

        var sb = new StringBuilder(256);
        if (output.Text is not null) sb.Append("T:").Append(output.Text);
        if (output.Plan is { } plan)
        {
            sb.Append("|P:").Append((int)plan.Kind).Append(':').Append(plan.Fingerprint ?? string.Empty);
            foreach (var target in plan.Targets)
            {
                sb.Append('|').Append(target.Name ?? string.Empty).Append('=')
                  .Append(Canonical(target.Payload));
            }
        }

        return hash(sb.ToString());
    }
}
