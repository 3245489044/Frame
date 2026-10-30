#nullable enable

using System;
using System.Collections.Generic;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 模板**能力面**（唯一解析点）：把「模板键是否存在」从散落的 <c>ContainsKey</c> 分支
/// 收敛为一次解析的能力位；能力缺失 = 可见降级（禁静默切换定位语义）。
/// <para>能力判据载体 = 模板结构事实（键在册 ∨ 占位符在文本中），非执行者叙述。</para>
/// </summary>
public sealed class TemplateSurface
{
    /// <summary>成本注记占位符（能力位判据；不是模板键）。</summary>
    public const string CostLabelPlaceholder = "{?costLabel}";

    private static readonly IReadOnlyDictionary<string, string> NoTemplates =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private TemplateSurface(IReadOnlyDictionary<string, string> templates)
    {
        Templates = templates;
        HasRoundFold = HasTemplate("roundFold");
        HasLeadFold = HasTemplate("leadFold");
        HasSectionHeader = HasTemplate("sectionHeader");
        HasBlockItem = HasTemplate("blockItem");
        HasAttachItem = HasTemplate("attachItem");
        HasOperationPanel = HasTemplate("actionPanel");
        HasDegraded = HasTemplate("renderDegraded");
        HasEmpty = HasTemplate("emptyP");
        HasCostPlaceholder = HasCostSlot(templates);
    }

    /// <summary>生效模板集（只读）。</summary>
    public IReadOnlyDictionary<string, string> Templates { get; }

    /// <summary>轮次折叠能力（<c>roundFold</c>）。</summary>
    public bool HasRoundFold { get; }

    /// <summary>段首前导折叠能力（<c>leadFold</c>）。</summary>
    public bool HasLeadFold { get; }

    /// <summary>轮段模板能力（<c>sectionHeader</c>；缺失 ⇒ 无轮段承载）。</summary>
    public bool HasSectionHeader { get; }

    /// <summary>消息行模板能力（<c>blockItem</c>）。</summary>
    public bool HasBlockItem { get; }

    /// <summary>折叠行模板能力（<c>attachItem</c>）。</summary>
    public bool HasAttachItem { get; }

    /// <summary>操作面板能力（<c>actionPanel</c>）。</summary>
    public bool HasOperationPanel { get; }

    /// <summary>降级提示能力（<c>renderDegraded</c>）。</summary>
    public bool HasDegraded { get; }

    /// <summary>空态能力（<c>emptyP</c>）。</summary>
    public bool HasEmpty { get; }

    /// <summary>轮次成本注记能力（轮段模板声明 <c>{?costLabel}</c>）。</summary>
    public bool HasCostPlaceholder { get; }

    /// <summary>解析生效模板集（唯一入口）。</summary>
    public static TemplateSurface From(IReadOnlyDictionary<string, string>? templates)
        => new(templates ?? NoTemplates);

    /// <summary>取模板原文（未在册 ⇒ false）。</summary>
    public bool TryTemplate(string key, out string template)
    {
        if (Templates.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            template = value;
            return true;
        }

        template = string.Empty;
        return false;
    }

    private bool HasTemplate(string key) => TryTemplate(key, out _);

    /// <summary>成本注记能力：轮段模板（roundFold 优先，退化到 sectionHeader）声明了该占位符。</summary>
    private static bool HasCostSlot(IReadOnlyDictionary<string, string> templates)
    {
        foreach (var key in new[] { "roundFold", "sectionHeader" })
        {
            if (templates.TryGetValue(key, out var text)
                && text.Contains(CostLabelPlaceholder, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
