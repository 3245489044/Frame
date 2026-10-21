#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace UiBackend.Html;

/// <summary>数据页源分组（组键 + 显示标签 + 行；采集序 = 渲染序）。</summary>
public sealed record HtmlDataGroup<TKey, TRow>(TKey Key, string Label, IReadOnlyList<TRow> Rows);

/// <summary>分页后的分组（<paramref name="Total"/> = 过滤后全量口径的组行数；<paramref name="Rows"/> = 本页行）。</summary>
public sealed record HtmlPagedGroup<TKey, TRow>(TKey Key, string Label, int Total, IReadOnlyList<TRow> Rows);

/// <summary>数据页分页产物（夹取后的页码 / 总页数 / 本页可见行数 / 分组）。</summary>
public sealed record HtmlDataPageSlice<TKey, TRow>(
    int Page,
    int PageCount,
    int VisibleRows,
    IReadOnlyList<HtmlPagedGroup<TKey, TRow>> Groups);

/// <summary>
/// 通用数据页运算：扁平化 → 可选过滤/排序 → 页码夹取 → 分页切片 → 按组键重组（含组总数）。
/// <para>
/// 领域只提供分组/选择器/谓词；分页与组计数口径单源在此。组顺序 = 采集序（LINQ GroupBy 稳定序），
/// 页大小 <c>&lt;= 0</c> 表示**不分页**（返回全量单页）。
/// </para>
/// </summary>
public static class DataPagePlan
{
    /// <summary>构建分页产物。</summary>
    public static HtmlDataPageSlice<TKey, TRow> Build<TKey, TRow>(
        IReadOnlyList<HtmlDataGroup<TKey, TRow>> groups,
        int page,
        int pageSize,
        Func<TRow, string, bool>? matches = null,
        string? filter = null,
        string? sortKey = null,
        string sortOrder = "asc",
        Func<TRow, string?>? keyOf = null,
        Func<TRow, string?>? updatedOf = null,
        IEqualityComparer<TKey>? keyComparer = null)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var equality = keyComparer ?? EqualityComparer<TKey>.Default;

        // 扁平化 + 组标签表（同组键取首次出现者，与采集序口径一致）。
        var labels = new Dictionary<TKey, string>(equality);
        var flat = new List<(TKey Key, TRow Row)>();
        foreach (var group in groups)
        {
            if (group is null) continue;
            if (!labels.ContainsKey(group.Key)) labels[group.Key] = group.Label ?? string.Empty;
            if (group.Rows is null) continue;
            foreach (var row in group.Rows) flat.Add((group.Key, row));
        }

        IEnumerable<(TKey Key, TRow Row)> query = flat;
        if (!string.IsNullOrWhiteSpace(filter) && matches is not null)
        {
            var keyword = filter!.Trim();
            query = query.Where(x => matches(x.Row, keyword));
        }

        var desc = string.Equals(sortOrder, "desc", StringComparison.OrdinalIgnoreCase);
        if (sortKey == "key" && keyOf is not null)
        {
            query = desc
                ? query.OrderByDescending(x => keyOf(x.Row) ?? string.Empty, StringComparer.Ordinal)
                : query.OrderBy(x => keyOf(x.Row) ?? string.Empty, StringComparer.Ordinal);
        }
        else if (sortKey == "updated" && updatedOf is not null)
        {
            query = desc
                ? query.OrderByDescending(x => updatedOf(x.Row) ?? string.Empty, StringComparer.Ordinal)
                : query.OrderBy(x => updatedOf(x.Row) ?? string.Empty, StringComparer.Ordinal);
        }

        var ordered = query.ToList();
        var pageRows = Slice(ordered, page, pageSize, out var clampedPage, out var pageCount);

        // 组总数 = 过滤后全量口径（一次扫描；不再每组重扫全量）。
        var totals = new Dictionary<TKey, int>(equality);
        foreach (var (key, _) in ordered)
        {
            totals.TryGetValue(key, out var current);
            totals[key] = current + 1;
        }

        var paged = new List<HtmlPagedGroup<TKey, TRow>>();
        foreach (var group in pageRows.GroupBy(x => x.Key, equality))
        {
            var rows = new List<TRow>();
            foreach (var item in group) rows.Add(item.Row);
            paged.Add(new HtmlPagedGroup<TKey, TRow>(
                group.Key,
                labels.TryGetValue(group.Key, out var label) ? label : string.Empty,
                totals.TryGetValue(group.Key, out var total) ? total : rows.Count,
                rows));
        }

        return new HtmlDataPageSlice<TKey, TRow>(clampedPage, pageCount, pageRows.Count, paged);
    }

    /// <summary>分页切片（<paramref name="pageSize"/> &lt;= 0 ⇒ 不分页，单页全量）。</summary>
    private static List<(TKey Key, TRow Row)> Slice<TKey, TRow>(
        List<(TKey Key, TRow Row)> rows, int page, int pageSize, out int clampedPage, out int pageCount)
    {
        if (pageSize <= 0)
        {
            clampedPage = 0;
            pageCount = 1;
            return rows;
        }

        (clampedPage, pageCount) = DataPageQuery.ClampPage(page, rows.Count, pageSize);
        return DataPageQuery.SlicePage(rows, clampedPage, pageSize);
    }
}
