#nullable enable

using System;
using System.Collections.Generic;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 键契约（KeyContract）——把 <see cref="HtmlKeyGuard"/> 从「<c>&lt;details&gt;</c> 有没有键」升级为
/// **全元素 × 同父键唯一性**：同一父级下，参与对齐的稳定键（<see cref="HtmlKey.NodeKey"/>：IdentityKey ∪ NamedAnchor）
/// 必须唯一；重复即违规（可装配期 fail-fast 或门禁判据）。
/// <para>纯结构扫描（零领域语义、零状态），对象域 = 模板文本；判据载体 = 解析后的节点树（结构事实，非作者叙述）。</para>
/// </summary>
public static class HtmlKeyContract
{
    /// <summary>校验单个模板：返回违规描述（空 = 通过）。</summary>
    public static IReadOnlyList<string> ValidateTemplate(string templateName, string? html)
    {
        var violations = new List<string>();
        if (string.IsNullOrEmpty(html)) return violations;
        var root = HtmlNodeParser.Parse(html);
        Walk(templateName, root, violations);
        return violations;
    }

    /// <summary>逐父级收集身份键 → 同父重复即违规；递归全树。</summary>
    private static void Walk(string templateName, UiNode node, List<string> violations)
    {
        if (node.Children is not { Count: > 0 }) return;
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var child in node.Children)
        {
            var key = HtmlKey.NodeKey.Derive(child);
            if (key is null) continue;
            if (seen.TryGetValue(key, out var count))
            {
                seen[key] = count + 1;
                violations.Add(
                    $"{templateName}: 同父键冲突 [{key}]（同父第 {count + 1} 个）—— 身份键/具名锚在同一父级必须唯一");
            }
            else
            {
                seen[key] = 1;
            }
        }
        foreach (var child in node.Children)
            Walk(templateName, child, violations);
    }

    /// <summary>校验整套模板：同父唯一性 + 既有稳定键存在性（<see cref="HtmlKeyGuard"/> 判据保留）。</summary>
    public static IReadOnlyList<string> ValidateAll(IReadOnlyDictionary<string, string>? templates)
    {
        var all = new List<string>();
        if (templates is null) return all;
        foreach (var kv in templates)
            all.AddRange(ValidateTemplate(kv.Key, kv.Value));
        all.AddRange(HtmlKeyGuard.ValidateAll(templates));
        return all;
    }

    /// <summary>强制校验：有违规即 fail-fast 抛 <see cref="InvalidOperationException"/>（含修复引导）。</summary>
    public static void Enforce(IReadOnlyDictionary<string, string>? templates)
        => Enforce(ValidateAll(templates));

    /// <summary>强制校验（复用已有扫描结果）；非空即抛。</summary>
    public static void Enforce(IReadOnlyList<string>? violations)
    {
        if (violations is { Count: > 0 })
            throw new InvalidOperationException(
                "HTML 模板键契约失败（同父键冲突 / 缺稳定键；键由渲染层派生，同父身份键必须唯一）：\n - " +
                string.Join("\n - ", violations));
    }
}
