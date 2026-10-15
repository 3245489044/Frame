using System;
using System.Linq;
using Strategy.Catalog.Core;
using Strategy.Catalog.Model;

namespace Strategy.Catalog.Json;

/// <summary>目录查询 JSON 门面：明细 / 检索 / 统计与投影（单一实现）。</summary>
public static partial class CatalogJson
{
    /// <summary>明细（按需展开）；未命中返回 <c>found=false</c> + 候选键引导（不静默返空）。</summary>
    public static string Describe(CatalogQueryService service, string? sourceId, string? key)
    {
        var detail = service.DescribeWithHint(sourceId, key, out var hint);
        return Serialize(new
        {
            status = "ok",
            op = OpDescribe,
            tier = "detail",
            sourceId,
            key,
            found = detail is not null,
            detail = detail is null ? null : new
            {
                key = detail.Key,
                display = detail.Display,
                summary = detail.Summary,
                invokeRef = detail.InvokeRef,
                fields = detail.FieldsOrEmpty.Select(f => new { name = f.Name, value = f.Value }),
            },
            hint = hint.Candidates.Count > 0 ? new { hint.Hint, candidates = hint.Candidates } : null,
        });
    }

    /// <summary>单源检索。</summary>
    public static string Search(CatalogQueryService service, string? sourceId, string? query, int? limit)
    {
        var result = service.Search(sourceId, query, limit ?? CatalogGrouper.DefaultSearchLimit);
        return Serialize(new
        {
            status = "ok",
            op = OpSearch,
            tier = "entry",
            sourceId = result.SourceId,
            query = result.Query,
            count = result.Count,
            truncated = result.Truncated,
            hits = result.Hits.Select(ProjectHit),
        });
    }

    /// <summary>跨源检索（不确定在哪本册子时的兜底入口）。</summary>
    public static string SearchAll(CatalogQueryService service, string? query, int? limit)
    {
        var result = service.SearchAll(query, limit ?? CatalogGrouper.DefaultSearchLimit);
        return Serialize(new
        {
            status = "ok",
            op = OpSearchAll,
            tier = "entry",
            sourceId = result.SourceId,
            query = result.Query,
            count = result.Count,
            truncated = result.Truncated,
            hits = result.Hits.Select(ProjectHit),
            note = "跨源命中；按 hit.sourceId + key 走 describe 展开明细",
        });
    }

    /// <summary>统计概览。</summary>
    public static string Stats(CatalogQueryService service, string? sourceId)
    {
        var stats = service.Stats(sourceId);
        return Serialize(new
        {
            status = "ok",
            op = OpStats,
            tier = "index",
            sourceId = stats.SourceId,
            display = stats.Display,
            total = stats.Total,
            pinned = stats.Pinned,
            axes = stats.Axes.Select(a => new
            {
                id = a.AxisId,
                display = a.AxisDisplay,
                groupCount = a.GroupCount,
                itemCount = a.ItemCount,
            }),
            largestGroups = stats.LargestGroups.Select(ProjectGroup),
        });
    }

    private static object ProjectItem(CatalogItem item) => new
    {
        key = item.Key,
        display = item.EffectiveDisplay,
        summary = item.Summary,
        invokeRef = item.InvokeRef,
        pinned = item.Pinned ? true : (bool?)null,
        axes = (item.Axes ?? Array.Empty<CatalogAxisValue>()).Select(a => new { id = a.AxisId, value = a.Value }),
    };

    private static object ProjectGroup(CatalogGroupRow row) => new
    {
        axis = row.AxisId,
        axisDisplay = row.AxisDisplay,
        value = row.Value,
        display = row.Display,
        count = row.Count,
    };

    private static object ProjectHit(CatalogSearchHit hit) => new
    {
        sourceId = hit.SourceId,
        key = hit.Key,
        display = hit.Display,
        summary = hit.Summary,
        invokeRef = hit.InvokeRef,
    };
}
