#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace UiBackend.Html.Presentation;

/// <summary>
/// 模板目录（单源 + 校验 + 指纹）：
/// 默认模板（框架资产）⊕ 产品覆盖（声明配置）在**加载期**完成校验，
/// 运行期只消费已校验集合，禁止"边渲染边猜模板键"。
/// </summary>
public sealed record TemplateCatalog(
    IReadOnlyDictionary<string, string> Templates,
    IReadOnlyList<string> MissingRequired,
    IReadOnlyList<string> UndeclaredPlaceholders,
    string Fingerprint)
{
    /// <summary>是否可以安全渲染（无必需键缺失、无越界占位符）。</summary>
    public bool IsWellFormed => MissingRequired.Count == 0 && UndeclaredPlaceholders.Count == 0;

    /// <summary>编译模板集：default ⊕ overrides；未声明占位符按区域字段白名单校验。</summary>
    /// <param name="defaults">框架默认模板。</param>
    /// <param name="overrides">产品覆盖模板（按同名键覆盖）。</param>
    /// <param name="required">必需键（缺失 ⇒ 报告，不静默）。</param>
    /// <param name="contract">呈现契约（字段白名单；null ⇒ 不校验占位符）。</param>
    public static TemplateCatalog Compile(
        IReadOnlyDictionary<string, string>? defaults,
        IReadOnlyDictionary<string, string>? overrides,
        IReadOnlyCollection<string>? required = null,
        PresentationContract? contract = null)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (defaults is not null)
            foreach (var pair in defaults) merged[pair.Key] = pair.Value;
        if (overrides is not null)
            foreach (var pair in overrides) merged[pair.Key] = pair.Value;

        var missing = (required ?? Array.Empty<string>())
            .Where(key => !string.IsNullOrWhiteSpace(key) && !merged.ContainsKey(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var undeclared = contract is null
            ? Array.Empty<string>()
            : merged
                .SelectMany(pair => ExtractPlaceholders(pair.Value)
                    .Where(field => !IsFrameworkField(field) && !contract.IsVisible(field))
                    .Select(field => pair.Key + ":" + field))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        return new TemplateCatalog(merged, missing, undeclared, ComputeFingerprint(merged));
    }

    /// <summary>模板占位符提取（`{field}`；排除 `{#each}` / `{/each}` / `{:else}` / `{>partial}` / `{?flag}`）。</summary>
    public static IReadOnlyList<string> ExtractPlaceholders(string? template)
    {
        if (string.IsNullOrWhiteSpace(template)) return Array.Empty<string>();
        var result = new List<string>();
        foreach (Match match in PlaceholderPattern.Matches(template))
        {
            var name = match.Groups[1].Value;
            if (name.Length == 0 || name.StartsWith('#')) continue;
            if (result.Contains(name, StringComparer.Ordinal)) continue;
            result.Add(name);
        }

        return result;
    }

    /// <summary>框架内建字段（区域无关；契约不裁）。</summary>
    public static bool IsFrameworkField(string field)
        => field is "key" or "class" or "role" or "template" or "anchor" or "badge" or "presentation"
            or "presentationOrigin" or "presentationUnknown" or "answerTrace" or "answerTraceLabel"
            or "messages" or "roundTitle" or "costLabel" or "rows" or "items" or "text" or "raw";

    private static readonly Regex PlaceholderPattern =
        new(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string ComputeFingerprint(IReadOnlyDictionary<string, string> templates)
    {
        var builder = new StringBuilder();
        foreach (var pair in templates.OrderBy(p => p.Key, StringComparer.Ordinal))
            builder.Append(pair.Key).Append('=').Append(pair.Value).Append('\n');
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash);
    }
}
