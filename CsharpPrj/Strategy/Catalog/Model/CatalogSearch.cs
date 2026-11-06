using System;
using System.Collections.Generic;
using System.Linq;

namespace Strategy.Catalog.Model;

/// <summary>
/// 检索命中行（<see cref="CatalogTier.Entry"/>）—— 关键词命中的条目摘要。
/// </summary>
/// <param name="SourceId">目录源标识（跨源检索时用于归因）。</param>
/// <param name="Key">条目键。</param>
/// <param name="Display">显示名。</param>
/// <param name="Summary">摘要（可空）。</param>
/// <param name="InvokeRef">执行体轨（可空）。</param>
public sealed record CatalogSearchHit(
    string SourceId, string Key, string Display, string? Summary, string? InvokeRef)
{
    /// <summary>本视图分级（<see cref="CatalogTier.Entry"/>）。</summary>
    public CatalogTier Tier => CatalogTier.Entry;

    /// <summary>由条目构造命中行。</summary>
    public static CatalogSearchHit From(string sourceId, CatalogItem item)
        => new(sourceId, item.Key, item.EffectiveDisplay, item.Summary, item.InvokeRef);
}

/// <summary>
/// 检索结果 —— 命中集 + 是否被上限截断（**不静默截断**：超限在结果里可见）。
/// </summary>
/// <param name="SourceId">目录源标识（跨源检索为 <c>*</c>）。</param>
/// <param name="Query">检索词（空 = 列出全部，此时通常由调用方限流）。</param>
/// <param name="Hits">命中行（序 = 命中序）。</param>
/// <param name="Truncated">是否因上限被截断（命中数超上限）。</param>
public sealed record CatalogSearchResult(
    string SourceId, string? Query, IReadOnlyList<CatalogSearchHit> Hits, bool Truncated = false)
{
    /// <summary>本视图分级（<see cref="CatalogTier.Entry"/>）。</summary>
    public CatalogTier Tier => CatalogTier.Entry;

    /// <summary>命中数。</summary>
    public int Count => Hits?.Count ?? 0;
}

/// <summary>
/// 单轴统计（<see cref="CatalogTier.Index"/> 的量化视图）—— 该轴有几个取值、覆盖多少条目。
/// </summary>
/// <param name="AxisId">轴标识。</param>
/// <param name="AxisDisplay">轴显示名。</param>
/// <param name="GroupCount">轴取值（分组）数。</param>
/// <param name="ItemCount">参与该轴分组的条目数（不含未挂载该轴的条目）。</param>
public sealed record CatalogAxisCount(string AxisId, string AxisDisplay, int GroupCount, int ItemCount);

/// <summary>
/// 统计概览（<see cref="CatalogTier.Index"/>）—— 规模与分布的一次性答案（对齐「字典有多少字/多少部首」）。
/// </summary>
/// <param name="SourceId">目录源标识。</param>
/// <param name="Display">目录源显示名。</param>
/// <param name="Total">条目总数。</param>
/// <param name="Pinned">常驻条目数。</param>
/// <param name="Axes">各轴统计（保序）。</param>
/// <param name="LargestGroups">最大分组（跨轴取前 N，用于快速判断体量集中处）。</param>
public sealed record CatalogStats(
    string SourceId,
    string Display,
    int Total,
    int Pinned,
    IReadOnlyList<CatalogAxisCount> Axes,
    IReadOnlyList<CatalogGroupRow> LargestGroups)
{
    /// <summary>本视图分级（<see cref="CatalogTier.Index"/>）。</summary>
    public CatalogTier Tier => CatalogTier.Index;

    /// <summary>空统计（源未装配；避免调用方判空）。</summary>
    public static CatalogStats Empty(string sourceId, string? display = null)
        => new(sourceId, CatalogText.Or(display, sourceId),
            0, 0, Array.Empty<CatalogAxisCount>(), Array.Empty<CatalogGroupRow>());
}

/// <summary>目录源概要（供「有哪些目录可查」这一最外层索引）。</summary>
/// <param name="SourceId">目录源标识。</param>
/// <param name="Display">显示名。</param>
/// <param name="AxisCount">轴数。</param>
/// <param name="Total">条目总数。</param>
public sealed record CatalogSourceInfo(string SourceId, string Display, int AxisCount, int Total);

/// <summary>目录条目键的补全建议（未命中时的可见化引导；不静默返空）。</summary>
/// <param name="Hint">引导文本。</param>
/// <param name="Candidates">候选键（去重、保序、截断后的少量样本）。</param>
public sealed record CatalogHint(string Hint, IReadOnlyList<string> Candidates)
{
    /// <summary>空建议。</summary>
    public static readonly CatalogHint None = new(string.Empty, Array.Empty<string>());

    /// <summary>由候选集构造建议。</summary>
    public static CatalogHint From(string hint, IEnumerable<string>? candidates, int limit = 8)
        => new(hint, (candidates ?? Array.Empty<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList());
}
