#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace UiBackend.Html;

/// <summary>
/// 通用「分组折叠列表」投影器 —— 把 <c>groups → rows</c> 两级结构投影为可折叠分组列表
/// （面板模板键由调用方参数化，行/组内层模板经模板引擎 <c>{&gt;name}</c> 命名调用解析）。
/// <para>
/// 纯模板投影 + 模型装填，<b>零领域类型依赖</b>：领域只把「分组标签 / 计数 / 行模型」交给本投影器，
/// 组内行字段（如 key/badge/title）仍由领域构造。空组集合 → 面板模板的 <c>{:else}</c> 空态分支生效。
/// </para>
/// </summary>
public static class HtmlGroupedListProjector
{
    /// <summary>单个分组（标签 + 计数 + 行模型列表）。</summary>
    /// <param name="Label">分组显示标签（如节点类型）。</param>
    /// <param name="Count">分组计数显示文本（调用方已格式化）。</param>
    /// <param name="Rows">组内行模型（供内层行模板绑定）。</param>
    public sealed record Group(string Label, string Count, IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);

    /// <summary>
    /// 渲染分组折叠列表面板：把 <paramref name="groups"/> 装填进 <paramref name="panelModel"/> 的
    /// <c>groups</c> 键（并置 <c>hasGroups</c> 判空位），再用 <paramref name="panelTemplateName"/> 模板投影。
    /// </summary>
    /// <param name="panelTemplateName">面板模板名（JSON templates 键，如 <c>taskNodesPanel</c>）。</param>
    /// <param name="panelModel">面板级模型（如 task/note）；本方法不修改传入字典，产出副本。</param>
    public static string Render(
        IReadOnlyDictionary<string, string> templates,
        string panelTemplateName,
        IReadOnlyDictionary<string, object?>? panelModel,
        IReadOnlyList<Group> groups)
    {
        var model = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        if (panelModel is not null)
            foreach (var kv in panelModel)
                model[kv.Key] = kv.Value;

        // 调用方若已在 panelModel 显式给出 hasGroups（如需保留领域判空语义），以其为准；否则按组数推导。
        if (!model.ContainsKey("hasGroups"))
            model["hasGroups"] = groups is { Count: > 0 } ? "1" : string.Empty;
        model["groups"] = groups is null
            ? new List<object?>()
            : groups.Select(g => (object?)new Dictionary<string, object?>
            {
                ["label"] = g.Label,
                ["count"] = g.Count,
                ["nodes"] = g.Rows,
            }).ToList();

        return HtmlTemplateEngine.Render(templates, panelTemplateName, model);
    }
}
