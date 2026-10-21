#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace UiBackend.Html;

/// <summary>
/// 通用「两级折叠树 / 平铺列表」投影器 —— 把「容器 → 行」结构投影为可折叠列表或平铺行列表。
/// <para>
/// 结构字段（id/open/selected/hasTasks/tasks/taskCount/hasNodes/nodes）由本投影器统一装填；
/// 领域只给行键/选中态/节点键集与额外字段（徽章等），模板键由调用方参数化。
/// 零领域类型依赖，与 <see cref="HtmlGroupedListProjector"/> 同族。
/// </para>
/// </summary>
public static class HtmlHierarchyListProjector
{
    /// <summary>单行（键 + 选中态 + 所属容器 + 关联节点键 + 额外字段）。</summary>
    public sealed record Row(
        string Key,
        bool Active = false,
        bool Selected = false,
        string? HistoryId = null,
        IReadOnlyList<string>? Nodes = null,
        IReadOnlyDictionary<string, object?>? Extra = null);

    /// <summary>单容器（Id + 展开/选中态 + 行集 + 聚合节点键 + 计数文本 + 额外字段）。</summary>
    public sealed record Container(
        string Id,
        bool Open = false,
        bool Selected = false,
        IReadOnlyList<Row>? Rows = null,
        IReadOnlyList<string>? Nodes = null,
        string? CountText = null,
        IReadOnlyDictionary<string, object?>? Extra = null);

    /// <summary>
    /// 渲染两级折叠树：容器经 <paramref name="containerTemplate"/> 投影，行经 <paramref name="rowTemplate"/> 投影。
    /// 空容器集 ⇒ <see cref="HtmlEmptyState"/> + <paramref name="emptyText"/>（为空则输出空串）。
    /// </summary>
    public static string RenderTree(
        IReadOnlyDictionary<string, string> templates,
        string containerTemplate,
        string rowTemplate,
        IReadOnlyList<Container>? containers,
        string? emptyText = null)
    {
        if (containers is null || containers.Count == 0)
            return string.IsNullOrEmpty(emptyText) ? string.Empty : HtmlEmptyState.Render(templates, emptyText!);

        var sb = new StringBuilder(containers.Count * 64);
        foreach (var container in containers)
        {
            var rows = BuildRows(container.Rows);
            var nodes = container.Nodes ?? Array.Empty<string>();
            var model = NewModel(container.Extra);
            model["id"] = container.Id;
            model["open"] = container.Open ? "1" : string.Empty;
            model["selected"] = container.Selected ? "1" : string.Empty;
            model["hasTasks"] = rows.Count > 0 ? "1" : string.Empty;
            model["tasks"] = rows;
            model["hasHistoryNodes"] = nodes.Count > 0 ? "1" : string.Empty;
            model["historyNodes"] = nodes;
            model["taskCount"] = container.CountText
                ?? (rows.Count > 0 ? rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty);
            sb.Append(HtmlTemplateEngine.Render(templates, containerTemplate, model));
        }

        return sb.ToString();
    }

    /// <summary>渲染平铺行列表：空行集 ⇒ <see cref="HtmlEmptyState"/> + <paramref name="emptyText"/>。</summary>
    public static string RenderList(
        IReadOnlyDictionary<string, string> templates,
        string rowTemplate,
        IReadOnlyList<Row>? rows,
        string? emptyText = null)
    {
        if (rows is null || rows.Count == 0)
            return string.IsNullOrEmpty(emptyText) ? string.Empty : HtmlEmptyState.Render(templates, emptyText!);

        var sb = new StringBuilder(rows.Count * 80);
        foreach (var row in rows)
            sb.Append(HtmlTemplateEngine.Render(templates, rowTemplate, BuildRowModel(row)));
        return sb.ToString();
    }

    private static List<object?> BuildRows(IReadOnlyList<Row>? rows)
    {
        if (rows is null || rows.Count == 0) return new List<object?>();
        var list = new List<object?>(rows.Count);
        foreach (var row in rows) list.Add(BuildRowModel(row));
        return list;
    }

    private static Dictionary<string, object?> BuildRowModel(Row row)
    {
        var model = NewModel(row.Extra);
        model["session"] = row.Key;
        model["active"] = row.Active ? "1" : string.Empty;
        model["selected"] = row.Selected || row.Active ? "1" : string.Empty;
        if (!string.IsNullOrWhiteSpace(row.HistoryId)) model["history"] = row.HistoryId;
        var nodes = row.Nodes;
        model["hasNodes"] = nodes is { Count: > 0 } ? "1" : string.Empty;
        if (nodes is { Count: > 0 }) model["nodes"] = nodes;
        return model;
    }

    private static Dictionary<string, object?> NewModel(IReadOnlyDictionary<string, object?>? extra)
    {
        var model = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (extra is not null)
            foreach (var kv in extra)
                model[kv.Key] = kv.Value;
        return model;
    }
}
