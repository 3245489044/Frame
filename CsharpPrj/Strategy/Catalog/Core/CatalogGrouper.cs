using System;
using System.Collections.Generic;
using System.Linq;
using Strategy.Catalog.Model;
using Strategy.Catalog.Ports;

namespace Strategy.Catalog.Core;

/// <summary>
/// 目录分组器（**纯函数 / 单一实现 / 零 IO**）—— 分级查询五件事的全部算法：
/// <c>索引 → 目录 → 明细 → 检索 → 统计</c>。
/// <para>
/// 这是「六套目录收敛为一套」的落点：各域只提供 <see cref="ICatalogSource"/>（数据），
/// 分组口径、排序、检索、统计、未命中引导全部走这里，**禁在任何域重写漂移版本**。
/// </para>
/// <para>
/// 分级纪律（有界量化）：索引/目录只出摘要行；明细只在 <see cref="Describe"/> 按需展开；
/// 检索有上限且**截断可见**（<see cref="CatalogSearchResult.Truncated"/>），不静默丢结果。
/// </para>
/// </summary>
public static partial class CatalogGrouper
{
    /// <summary>跨源检索的源标识（<see cref="SearchAll"/> 产物）。</summary>
    public const string AllSources = "*";

    /// <summary>检索默认上限（防止一次检索把整册字典倒出来）。</summary>
    public const int DefaultSearchLimit = 50;

    /// <summary>统计里「最大分组」默认条数。</summary>
    public const int DefaultLargestGroupLimit = 10;

    // ── 一级索引 ────────────────────────────────────────────────────────────

    /// <summary>构造一级索引：源概要 + 常驻条目平铺 + 各轴目录行（**不含明细**）。</summary>
    public static CatalogIndex Index(ICatalogSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var items = Items(source);
        var axes = source.Axes ?? Array.Empty<CatalogAxis>();

        var pinned = items
            .Where(i => i.Pinned)
            .OrderBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var groups = new List<CatalogGroupRow>();
        foreach (var axis in axes)
        {
            if (axis is null || string.IsNullOrWhiteSpace(axis.Id)) continue;
            groups.AddRange(Group(items, axis));
        }

        return new CatalogIndex(source.SourceId, SourceDisplay(source), items.Count, axes, pinned, groups);
    }

    // ── 二级目录 ────────────────────────────────────────────────────────────

    /// <summary>按轴取值列出条目摘要（轴取值大小写不敏感；轴未声明抛 UnknownCatalogAxisException）。</summary>
    public static CatalogList List(ICatalogSource source, string? axisId, string? value)
    {
        ArgumentNullException.ThrowIfNull(source);
        var axis = RequireAxis(source, axisId);
        var items = Items(source)
            .Where(i => MatchesAxis(i, axis, value))
            .OrderBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var v = value ?? string.Empty;
        return new CatalogList(source.SourceId, axis.Id, axis.EffectiveDisplay, v,
            GroupValueDisplay(items, axis, v), items);
    }

    /// <summary>单轴目录行（按计数降序 → 取值序）。</summary>
    public static IReadOnlyList<CatalogGroupRow> Group(ICatalogSource source, string? axisId)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Group(Items(source), RequireAxis(source, axisId));
    }

    // ── 明细 ────────────────────────────────────────────────────────────────

    /// <summary>取条目明细（按需展开）；未命中返回 null，未命中引导见 <see cref="HintFor"/>。</summary>
    public static CatalogDetail? Describe(ICatalogSource source, string? key)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(key)) return null;
        var detail = source.Describe(key!);
        return detail is null
            ? null
            : detail with { Display = CatalogText.Or(detail.Display, key!) };
    }

    /// <summary>未命中引导：给出"最像"的候选键（前缀命中优先，其次包含命中），不静默返空。</summary>
    public static CatalogHint HintFor(ICatalogSource source, string? key)
    {
        ArgumentNullException.ThrowIfNull(source);
        var items = Items(source);
        if (items.Count == 0) return new CatalogHint($"目录源 '{source.SourceId}' 无条目。", Array.Empty<string>());
        if (string.IsNullOrWhiteSpace(key))
            return CatalogHint.From("缺少条目键；候选如下：", items.Select(i => i.Key));

        var k = key!.Trim();
        var prefix = items.Where(i => i.Key.StartsWith(k, StringComparison.OrdinalIgnoreCase))
                          .Select(i => i.Key).ToList();
        if (prefix.Count > 0) return CatalogHint.From($"'{k}' 未精确命中；同前缀候选：", prefix);

        var contains = items.Where(i => i.Key.Contains(k, StringComparison.OrdinalIgnoreCase))
                            .Select(i => i.Key).ToList();
        if (contains.Count > 0) return CatalogHint.From($"'{k}' 未命中；包含该片段的候选：", contains);

        return CatalogHint.From($"'{k}' 未命中；本目录条目样本：", items.Select(i => i.Key));
    }
}
