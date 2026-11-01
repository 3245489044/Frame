#nullable enable

using System;
using System.Collections.Generic;
using Share.Conversation.Presentation;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 模板**能力面**（唯一解析点）：把「模板键是否存在」从散落的 <c>ContainsKey</c> 分支
/// 收敛为一次解析的能力位；能力缺失 = 可见降级（禁静默切换定位语义）。
/// <para>能力判据载体 = 模板结构事实（声明区域键在册 ∧ 模板键在册），非执行者叙述。</para>
/// <para>区域 → 模板键映射单一来源 = 声明面（<see cref="PresentationRegions"/>）；
/// 本类不持有任何具体模板名字面量。</para>
/// </summary>
public sealed class TemplateSurface
{
    /// <summary>成本注记占位符（能力位判据；不是模板键）。</summary>
    public const string CostLabelPlaceholder = "{?costLabel}";

    private static readonly IReadOnlyDictionary<string, string> NoTemplates =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private TemplateSurface(IReadOnlyDictionary<string, string> templates, PresentationRegions regions)
    {
        Templates = templates;
        Regions = regions ?? PresentationRegions.Empty;
        HasRoundFold = HasRegion(TemplateRegions.RoundFold);
        HasLeadFold = HasRegion(TemplateRegions.LeadFold);
        HasSectionHeader = HasRegion(TemplateRegions.SectionHeader);
        HasBlockItem = HasRegion(TemplateRegions.Row);
        HasAttachItem = HasRegion(TemplateRegions.AttachItem);
        HasOperationPanel = HasRegion(TemplateRegions.ActionPanel);
        HasDegraded = HasRegion(TemplateRegions.RenderDegraded);
        HasEmpty = HasRegion(TemplateRegions.EmptyP);
        HasCostPlaceholder = HasCostSlot(templates, Regions);
    }

    /// <summary>生效模板集（只读）。</summary>
    public IReadOnlyDictionary<string, string> Templates { get; }

    /// <summary>渲染区域声明（模板键单源）。</summary>
    public PresentationRegions Regions { get; }

    /// <summary>声明是否可用（false ⇒ 模板能力全缺，渲染链可见降级）。</summary>
    public bool IsDeclared => Regions.IsDeclared;

    /// <summary>未声明类的行模板兜底键（声明 defaults.template；空 ⇒ 可见降级）。</summary>
    public string DefaultTemplate => Regions.DefaultTemplate;

    /// <summary>轮次折叠能力。</summary>
    public bool HasRoundFold { get; }

    /// <summary>段首前导折叠能力。</summary>
    public bool HasLeadFold { get; }

    /// <summary>轮段模板能力（缺失 ⇒ 无轮段承载）。</summary>
    public bool HasSectionHeader { get; }

    /// <summary>消息行模板能力（声明行键在册）。</summary>
    public bool HasBlockItem { get; }

    /// <summary>折叠行模板能力。</summary>
    public bool HasAttachItem { get; }

    /// <summary>操作面板能力。</summary>
    public bool HasOperationPanel { get; }

    /// <summary>降级提示能力。</summary>
    public bool HasDegraded { get; }

    /// <summary>空态能力。</summary>
    public bool HasEmpty { get; }

    /// <summary>轮次成本注记能力（轮段模板声明 <c>{?costLabel}</c>）。</summary>
    public bool HasCostPlaceholder { get; }

    /// <summary>区域 → 声明模板键（未声明 ⇒ 空串）。</summary>
    /// <param name="region">区域标识（<see cref="TemplateRegions"/>）。</param>
    public string KeyOf(string region) => Regions.KeyOf(region);

    /// <summary>解析生效模板集（唯一入口）。</summary>
    /// <param name="templates">模板集（null ⇒ 空集）。</param>
    /// <param name="regions">渲染区域声明（null ⇒ 空声明：能力全缺，可见降级）。</param>
    public static TemplateSurface From(
        IReadOnlyDictionary<string, string>? templates, PresentationRegions? regions = null)
        => new(templates ?? NoTemplates, regions ?? PresentationRegions.Empty);

    /// <summary>取模板原文（未在册 ⇒ false）。</summary>
    public bool TryTemplate(string key, out string template)
    {
        if (!string.IsNullOrEmpty(key)
            && Templates.TryGetValue(key, out var value)
            && !string.IsNullOrWhiteSpace(value))
        {
            template = value;
            return true;
        }

        template = string.Empty;
        return false;
    }

    private bool HasRegion(string region) => TryTemplate(Regions.KeyOf(region), out _);

    /// <summary>成本注记能力：轮段模板（roundFold 优先，退化到 sectionHeader）声明了该占位符。</summary>
    private static bool HasCostSlot(
        IReadOnlyDictionary<string, string> templates, PresentationRegions regions)
    {
        foreach (var region in new[] { TemplateRegions.RoundFold, TemplateRegions.SectionHeader })
        {
            if (templates.TryGetValue(regions.KeyOf(region), out var text)
                && text.Contains(CostLabelPlaceholder, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

