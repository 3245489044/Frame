using System;
using System.Collections.Generic;
using System.Linq;
using Share.Composition.Ports;

namespace Share.Composition.Composers;

/// <summary>
/// 模板填槽组装 —— 把部件按「槽位名 → 内容」映射进模板占位符，经 <see cref="ITemplateRenderer"/> 渲染。
/// 对应 HTML 页面的 <c>{history}</c>/<c>{taskList}</c>/<c>{tree}</c> 等占位符注入路径（设计文档 §5「HTML」行）。
/// <para>
/// 槽位归约：同槽多部件按 <see cref="IComponent.Order"/> 升序、经 <see cref="KernelJoins.BlankLine"/> 归约为单段文本，
/// 再作为该槽的模型值（模板侧用 <c>{slot}</c> 编码注入 / <c>{slot|raw}</c> 受信原文注入）。
/// </para>
/// </summary>
public sealed class TemplateComposer
{
    private readonly ITemplateRenderer _renderer;

    /// <param name="renderer">模板渲染端口（消费侧适配器注入，如 HtmlTemplateRenderer）。</param>
    public TemplateComposer(ITemplateRenderer renderer)
        => _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));

    /// <summary>
    /// 把部件集归约为槽位模型：槽位名 → 该槽归约文本。
    /// 槽位名大小写不敏感去重（后出现的同名槽并入同一值），槽内保持 <see cref="IComponent.Order"/> 升序。
    /// </summary>
    /// <param name="parts">部件集（null/空 → 空模型）。</param>
    /// <param name="join">槽内归约策略（null → <see cref="KernelJoins.BlankLine"/>）。</param>
    public static IReadOnlyDictionary<string, object?> BuildModel(
        IReadOnlyList<IComponent>? parts,
        Func<IReadOnlyList<IComponent>, string>? join = null)
    {
        var model = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (parts is null || parts.Count == 0) return model;

        var reduce = join ?? KernelJoins.BlankLine;
        foreach (var group in parts.GroupBy(p => p.Slot, StringComparer.OrdinalIgnoreCase))
            model[group.Key] = reduce(group.OrderBy(p => p.Order).ToArray());

        return model;
    }

    /// <summary>按命名模板组装：部件集 → 槽位模型 → 渲染（模板缺失 → <paramref name="fallback"/>）。</summary>
    public string Compose(
        IReadOnlyDictionary<string, string> templates,
        string name,
        IReadOnlyList<IComponent>? parts,
        string fallback = "")
        => _renderer.Render(templates, name, BuildModel(parts), fallback);

    /// <summary>按模板串组装：部件集 → 槽位模型 → 渲染。</summary>
    public string ComposeTemplate(
        string? template,
        IReadOnlyList<IComponent>? parts,
        IReadOnlyDictionary<string, string>? templates = null)
        => _renderer.RenderTemplate(template, templates, BuildModel(parts));
}
