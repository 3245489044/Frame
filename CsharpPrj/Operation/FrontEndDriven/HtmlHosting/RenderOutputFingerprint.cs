#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Strategy.Refresh.Core;
using Strategy.Refresh.Ports;
using UiBackend.Abstractions;

namespace FrontEndDriven.HtmlHosting;

/// <summary>
/// 渲染产物指纹（通用实现）—— 把 <see cref="RenderOutput"/>（文本 / 写计划 / 键控树）规范化为稳定串后哈希。
/// <para>收敛领域侧自写的「产物 → canonical → hash」第二套实现；键控树按标签/键/有序属性/文本/子节点递归规范化。</para>
/// <para>纯函数、零领域类型；哈希实现由调用方注入（缺省 FNV-1a，与刷新框架同一口径）。</para>
/// </summary>
public static class RenderOutputFingerprint
{
    /// <summary>产物指纹（<paramref name="fingerprint"/> 缺省 = 刷新框架默认实现）。</summary>
    public static string Of(RenderOutput output, IFingerprint? fingerprint = null)
    {
        var hash = fingerprint ?? Fnv1aFingerprint.Default;
        if (output is null || output.IsSkip) return hash.Of("skip");

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

        return hash.Of(sb.ToString());
    }

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
        var sb = new StringBuilder(256);
        foreach (var part in parts) sb.Append(part ?? string.Empty).Append('\u0001');
        return sb.ToString();
    }
}
