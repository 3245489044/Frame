using System;
using System.Collections.Generic;
using System.Linq;

using TConfig;
using UiBackend.Html;

namespace FrontEndDriven.ProductionConsole.Console;

/// <summary>
/// 控制台声明集（模板集 + 页面组件目录）——渲染层只消费本对象：模板 / CSS / JS / 文案表 / 格式化器
/// 全部来自面板 JSON（<c>templates.*</c> + <c>htmlPage.components/formatters/labels</c>），
/// 渲染器不读配置、不拼 HTML；缺键即显式降级为诊断片段（禁静默回退内嵌兜底 HTML）。
/// <para>与 AgentRuntimeUI.json 同一标准：声明面（JSON）决定结构与文案，C# 只产数据。</para>
/// </summary>
public sealed class ConsoleTemplateSet
{
    /// <summary>空集（未装配 → 渲染输出诊断片段，不产出兜底页面）。</summary>
    public static readonly ConsoleTemplateSet Empty =
        new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), UiComponentCatalog.Empty);

    public ConsoleTemplateSet(
        IReadOnlyDictionary<string, string> templates,
        UiComponentCatalog catalog,
        IReadOnlyList<string>? regions = null)
    {
        Templates = templates ?? throw new ArgumentNullException(nameof(templates));
        Catalog = catalog ?? UiComponentCatalog.Empty;
        Regions = regions ?? Array.Empty<string>();
    }

    /// <summary>模板集（键大小写不敏感）。</summary>
    public IReadOnlyDictionary<string, string> Templates { get; }

    /// <summary>页面组件/文案/格式化目录（派生模型与查表）。</summary>
    public UiComponentCatalog Catalog { get; }

    /// <summary>面板声明的区域键（写回目标合法域）；未声明页面 → 空集（不校验）。</summary>
    public IReadOnlyList<string> Regions { get; }

    /// <summary>是否声明了区域（空集 = 未提供声明，跳过校验）。</summary>
    public bool HasRegions => Regions.Count > 0;

    /// <summary>区域是否为面板声明项（未声明 ⇒ 不得作为写回目标）。</summary>
    public bool IsDeclaredRegion(string region)
        => !HasRegions || Regions.Any(r => string.Equals(r, region, StringComparison.OrdinalIgnoreCase));

    /// <summary>取模板；缺失/空白 → null（调用方显式降级）。</summary>
    public string? Template(string? name)
        => !string.IsNullOrWhiteSpace(name) && Templates.TryGetValue(name!, out var template)
           && !string.IsNullOrWhiteSpace(template)
            ? template
            : null;

    /// <summary>
    /// 从整体 UI 声明装配（宿主控件 + htmlPage 名）：模板取顶层 <c>templates</c>，
    /// 组件目录取该 htmlPage 的 components/formatters/labels（区域式页面同样合法）。
    /// </summary>
    public static ConsoleTemplateSet From(UIConfiguration? ui, string hostControl, string pageName)
    {
        var templates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (ui?.Templates is { Count: > 0 } declared)
            foreach (var kv in declared)
                if (!string.IsNullOrWhiteSpace(kv.Key) && kv.Value is not null)
                    templates[kv.Key] = kv.Value;

        var page = ui is null ? null : HtmlDeclarationParser.FindPageDeclaration(ui, hostControl, pageName);
        var catalog = UiComponentCatalog.Empty;
        if (page is not null)
        {
            catalog = UiComponentCatalog.From(new UIConfiguration
            {
                Components = page.Components,
                Formatters = page.Formatters,
                Labels = page.Labels,
            });
        }

        return new ConsoleTemplateSet(
            templates,
            catalog,
            page?.Regions?.Select(r => r.Key).Where(k => !string.IsNullOrWhiteSpace(k)).ToList());
    }
}
