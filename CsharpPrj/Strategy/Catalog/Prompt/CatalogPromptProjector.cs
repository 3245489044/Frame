using System;
using System.Text;
using Strategy.Catalog.Core;

namespace Strategy.Catalog.Prompt;

/// <summary>
/// 提示词档投影器（CatalogPromptProjector）—— **纯函数 / 零领域字符串**：把 <see cref="CatalogQueryService"/>
/// 的 <c>index</c>/<c>list</c>/<c>describe</c> 三档结果投影为提示词可注入的档行文本。
/// <para>
/// 零新增查询实现（红线）：一切取数走 <see cref="CatalogQueryService"/>（唯一查询面）；
/// 本类只做「结果 → 文本」的确定性渲染，可单测、可 CI 对拍。
/// </para>
/// </summary>
public static class CatalogPromptProjector
{
    /// <summary>分组档默认条目上限（截断可见，不静默丢）。</summary>
    public const int DefaultGroupLimit = 50;

    /// <summary>
    /// 索引档：源概要 + 指定轴的目录行（`取值：计数`）。轴无分组 → 空串（不产空壳）。
    /// </summary>
    public static string Index(CatalogQueryService queryService, string sourceId, string axisId)
    {
        ArgumentNullException.ThrowIfNull(queryService);
        var index = queryService.Index(sourceId);
        var sb = new StringBuilder();
        sb.Append(index.SourceId).Append('（').Append(index.Total).Append(" 条）");
        var any = false;
        foreach (var row in index.Groups)
        {
            if (!string.Equals(row.AxisId, axisId, StringComparison.OrdinalIgnoreCase)) continue;
            any = true;
            sb.Append('\n').Append("- ").Append(row.Display).Append('：').Append(row.Count);
        }
        return any ? sb.ToString() : string.Empty;
    }

    /// <summary>
    /// 分组档：某轴取值下的条目摘要行（键 + 一句话），有上限且**截断可见**。
    /// </summary>
    public static string Group(
        CatalogQueryService queryService, string sourceId, string axisId, string value, int limit = DefaultGroupLimit)
    {
        ArgumentNullException.ThrowIfNull(queryService);
        var list = queryService.List(sourceId, axisId, value);
        var sb = new StringBuilder();
        sb.Append(list.SourceId).Append('/').Append(list.AxisDisplay).Append(' ').Append(list.Value)
          .Append('（').Append(list.Count).Append(" 条）");

        var shown = 0;
        foreach (var item in list.Items)
        {
            if (shown >= limit)
            {
                sb.Append('\n').Append("…（截断，共 ").Append(list.Count).Append(" 条）");
                break;
            }
            sb.Append('\n').Append("- ").Append(item.Key);
            if (!string.IsNullOrWhiteSpace(item.Summary)) sb.Append('：').Append(item.Summary);
            shown++;
        }
        return sb.ToString();
    }

    /// <summary>
    /// 明细档：单条完整信息（字段逐行）；未命中 → 未命中引导文本（不静默返空）。
    /// </summary>
    public static string Entry(CatalogQueryService queryService, string sourceId, string key)
    {
        ArgumentNullException.ThrowIfNull(queryService);
        var detail = queryService.DescribeWithHint(sourceId, key, out var hint);
        if (detail is null)
            return hint is null || string.IsNullOrWhiteSpace(hint.Hint) ? string.Empty : hint.Hint;

        var sb = new StringBuilder();
        sb.Append(detail.Key).Append('：').Append(detail.Display);
        if (!string.IsNullOrWhiteSpace(detail.Summary)) sb.Append('\n').Append(detail.Summary);
        foreach (var field in detail.FieldsOrEmpty)
            sb.Append('\n').Append("- ").Append(field.Name).Append('：').Append(field.Value);
        return sb.ToString();
    }
}
