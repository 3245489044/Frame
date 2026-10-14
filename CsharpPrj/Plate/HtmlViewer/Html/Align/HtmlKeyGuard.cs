#nullable enable

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace UiBackend.Html;

/// <summary>
/// 编译期键守卫（I2 键确定性）——校验模板集中「可保持状态节点」（&lt;details&gt; 等）是否携带框架认可的**稳定键**
/// （data-name/data-history/data-session/data-round/data-pane/id），缺失即判违规（可 fail-fast 或仅诊断），
/// 从机制上杜绝位置路径回退（p:）导致的「重渲染后展开态初始化」。
/// <para>纯结构扫描（零领域语义、零状态）：对象域 = 模板文本；可作装配期断言或门禁判据。</para>
/// </summary>
public static class HtmlKeyGuard
{
    /// <summary>details 开标签（判据载体）。</summary>
    private static readonly Regex DetailsTag = new(@"<details\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>校验单个模板：返回违规描述（空 = 通过）。</summary>
    public static IReadOnlyList<string> ValidateTemplate(string templateName, string? html)
    {
        var violations = new List<string>();
        if (string.IsNullOrEmpty(html)) return violations;
        foreach (Match m in DetailsTag.Matches(html))
        {
            var tag = m.Value;
            var hasKey = false;
            foreach (var attr in HtmlKey.Attributes)
            {
                if (tag.IndexOf(attr + "=", StringComparison.OrdinalIgnoreCase) >= 0) { hasKey = true; break; }
            }
            if (!hasKey)
                violations.Add(templateName + ": <details> 缺稳定键（需 " + string.Join("/", HtmlKey.Attributes) + " 之一；禁位置回退）");
        }
        return violations;
    }

    /// <summary>校验整套模板：汇总全部违规。</summary>
    public static IReadOnlyList<string> ValidateAll(IReadOnlyDictionary<string, string>? templates)
    {
        var all = new List<string>();
        if (templates is null) return all;
        foreach (var kv in templates)
            all.AddRange(ValidateTemplate(kv.Key, kv.Value));
        return all;
    }

    /// <summary>强制校验：有违规即 fail-fast 抛 <see cref="InvalidOperationException"/>（含修复引导）。</summary>
    public static void Enforce(IReadOnlyDictionary<string, string>? templates)
        => Enforce(ValidateAll(templates));

    /// <summary>强制校验（复用已有扫描结果）：非空即抛——供装配期"先取违规明细做诊断、再单次抛出"，
    /// 消除 `ValidateAll` + `Enforce` 的双重扫描（C1 冗余）。</summary>
    public static void Enforce(IReadOnlyList<string>? violations)
    {
        if (violations is { Count: > 0 })
            throw new InvalidOperationException(
                "HTML 模板键守卫失败（缺稳定键的 <details> 在重渲染后展开态无法按 key 恢复）：\n - " +
                string.Join("\n - ", violations));
    }
}