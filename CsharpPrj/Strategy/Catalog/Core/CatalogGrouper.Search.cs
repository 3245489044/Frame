using System;
using System.Collections.Generic;
using System.Linq;
using Strategy.Catalog.Model;
using Strategy.Catalog.Ports;

namespace Strategy.Catalog.Core;

/// <summary>目录分组器：检索 / 统计 / 源概要（单一实现）。</summary>
public static partial class CatalogGrouper
{
    // ── 检索 ────────────────────────────────────────────────────────────────

    /// <summary>单源检索：命中键 / 显示名 / 摘要 / 标签 / 轴取值；空检索词 = 列全部（受限）。</summary>
    public static CatalogSearchResult Search(ICatalogSource source, string? query, int limit = DefaultSearchLimit)
    {
        ArgumentNullException.ThrowIfNull(source);
        return SearchCore(new[] { source }, source.SourceId, query, limit);
    }

    /// <summary>跨源检索（源序保序；上限作用于**总命中数**，截断可见）。</summary>
    public static CatalogSearchResult SearchAll(
        IEnumerable<ICatalogSource>? sources, string? query, int limit = DefaultSearchLimit)
        => SearchCore((sources ?? Enumerable.Empty<ICatalogSource>()).Where(s => s is not null),
            AllSources, query, limit);

    // ── 统计 ────────────────────────────────────────────────────────────────

    /// <summary>规模与分布概览（各轴分组数/覆盖数 + 最大分组）。</summary>
    public static CatalogStats Stats(ICatalogSource source, int largestGroupLimit = DefaultLargestGroupLimit)
    {
        ArgumentNullException.ThrowIfNull(source);
        var items = Items(source);
        var axes = source.Axes ?? Array.Empty<CatalogAxis>();

        var axisCounts = new List<CatalogAxisCount>();
        var allGroups = new List<CatalogGroupRow>();
        foreach (var axis in axes)
        {
            if (axis is null || string.IsNullOrWhiteSpace(axis.Id)) continue;
            var rows = Group(items, axis);
            axisCounts.Add(new CatalogAxisCount(axis.Id, axis.EffectiveDisplay, rows.Count, rows.Sum(r => r.Count)));
            allGroups.AddRange(rows);
        }

        var largest = allGroups
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.AxisId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Value, StringComparer.OrdinalIgnoreCase)
            .Take(largestGroupLimit < 0 ? 0 : largestGroupLimit)
            .ToList();

        return new CatalogStats(source.SourceId, SourceDisplay(source), items.Count,
            items.Count(i => i.Pinned), axisCounts, largest);
    }

    /// <summary>源概要（「有哪些目录可查」这一最外层索引）。</summary>
    public static CatalogSourceInfo Info(ICatalogSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CatalogSourceInfo(source.SourceId, SourceDisplay(source),
            (source.Axes ?? Array.Empty<CatalogAxis>()).Count(a => a is not null && !string.IsNullOrWhiteSpace(a.Id)),
            Items(source).Count);
    }

    // ── 检索内部 ────────────────────────────────────────────────────────────

    private static CatalogSearchResult SearchCore(
        IEnumerable<ICatalogSource> sources, string sourceId, string? query, int limit)
    {
        var effectiveLimit = limit <= 0 ? DefaultSearchLimit : limit;
        var hits = new List<CatalogSearchHit>();
        var truncated = false;
        var q = string.IsNullOrWhiteSpace(query) ? null : query.Trim();

        foreach (var source in sources)
        {
            foreach (var item in Items(source))
            {
                if (!Matches(item, q)) continue;
                if (hits.Count >= effectiveLimit) { truncated = true; break; }
                hits.Add(CatalogSearchHit.From(source.SourceId, item));
            }
            if (truncated) break;
        }

        return new CatalogSearchResult(sourceId, q, hits, truncated);
    }

    /// <summary>检索命中判定：空词 = 全命中；否则键/显示名/摘要/标签/轴取值任一包含即命中。</summary>
    private static bool Matches(CatalogItem item, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var q = query!;
        if (Contains(item.Key, q) || Contains(item.EffectiveDisplay, q) || Contains(item.Summary, q)) return true;
        foreach (var tag in item.Tags ?? Array.Empty<string>())
            if (Contains(tag, q)) return true;
        foreach (var axis in item.Axes ?? Array.Empty<CatalogAxisValue>())
        {
            if (axis is null) continue;
            if (Contains(axis.Value, q) || Contains(axis.Display, q)) return true;
        }
        return false;
    }

    private static bool Contains(string? value, string needle)
        => !string.IsNullOrWhiteSpace(value) && value!.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
