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
        => UiNodeCanonicalizer.Of(output, (fingerprint ?? Fnv1aFingerprint.Default).Of);

    /// <summary>单载荷规范化（唯一实现 = Plate 的 <see cref="UiNodeCanonicalizer"/>）。</summary>
    public static string Canonical(object? payload) => UiNodeCanonicalizer.Canonical(payload);

    /// <summary>键控树规范化（唯一实现 = Plate 的 <see cref="UiNodeCanonicalizer"/>）。</summary>
    public static string CanonicalNode(UiNode node) => UiNodeCanonicalizer.CanonicalNode(node);

    /// <summary>多段规范化拼接（唯一实现 = Plate 的 <see cref="UiNodeCanonicalizer"/>）。</summary>
    public static string Join(IEnumerable<string?> parts) => UiNodeCanonicalizer.Join(parts);
}
