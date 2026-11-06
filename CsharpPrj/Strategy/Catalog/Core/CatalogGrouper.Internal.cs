using System;
using System.Collections.Generic;
using System.Linq;
using Strategy.Catalog.Model;
using Strategy.Catalog.Ports;

namespace Strategy.Catalog.Core;

/// <summary>目录分组器内部件（共享实现：条目归一 / 轴校验 / 单轴分组）。</summary>
public static partial class CatalogGrouper
{
    /// <summary>目录源显示名（统一空值兜底入口）。</summary>
    private static string SourceDisplay(ICatalogSource source)
        => CatalogText.Or(source.Display, source.SourceId);

    /// <summary>条目全集（统一空值兜底 + 丢弃空键条目：空键无法寻址，属装配 bug 而非查询结果）。</summary>
    private static IReadOnlyList<CatalogItem> Items(ICatalogSource source)
    {
        var raw = source.Enumerate();
        if (raw is null || raw.Count == 0) return Array.Empty<CatalogItem>();
        var list = new List<CatalogItem>(raw.Count);
        foreach (var item in raw)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Key)) continue;
            list.Add(item.Axes is null ? item with { Axes = Array.Empty<CatalogAxisValue>() } : item);
        }
        return list;
    }

    /// <summary>按 Id 取声明轴；未声明抛（显式失败，避免"查不到却以为为空"）。</summary>
    private static CatalogAxis RequireAxis(ICatalogSource source, string? axisId)
    {
        var axes = source.Axes ?? Array.Empty<CatalogAxis>();
        var axis = axes.FirstOrDefault(a => a is not null
            && string.Equals(a.Id, axisId, StringComparison.OrdinalIgnoreCase));
        if (axis is null)
        {
            throw new Diagnostics.UnknownCatalogAxisException(source.SourceId, axisId ?? string.Empty,
                axes.Where(a => a is not null).Select(a => a.Id).ToList());
        }
        return axis;
    }

    private static bool MatchesAxis(CatalogItem item, CatalogAxis axis, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var mine = item.AxisValueOf(axis.Id)?.Value;   // 轴值查找唯一入口（与 Group / List 同一来源）
        if (string.IsNullOrWhiteSpace(mine)) return false;
        return string.Equals(mine, value, axis.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 二级目录取值显示名：取该组首个非空 <c>Display</c>；无 → 观测到的轴取值；再 → 请求值。
    /// （避免把 <c>Display</c> 直接赋成原始 <c>Value</c>，让 <c>list</c> 与 <c>group</c> 口径一致）
    /// </summary>
    private static string GroupValueDisplay(IReadOnlyList<CatalogItem> items, CatalogAxis axis, string requested)
    {
        string? observed = null;
        foreach (var item in items)
        {
            var axisValue = item.AxisValueOf(axis.Id);
            if (axisValue is null || string.IsNullOrWhiteSpace(axisValue.Value)) continue;
            observed ??= axisValue.Value;
            if (!string.IsNullOrWhiteSpace(axisValue.Display)) return axisValue.Display!;
        }
        return observed ?? requested;
    }

    /// <summary>单轴分组（计数降序 → 取值序；取值显示名取该组首个非空 display）。</summary>
    private static IReadOnlyList<CatalogGroupRow> Group(IReadOnlyList<CatalogItem> items, CatalogAxis axis)
    {
        var comparer = axis.CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var buckets = new Dictionary<string, Bucket>(comparer);
        foreach (var item in items)
        {
            var value = item.ValueOf(axis.Id);
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (!buckets.TryGetValue(value, out var bucket))
            {
                bucket = new Bucket(value);
                buckets[value] = bucket;
            }
            bucket.Count++;
            bucket.Display ??= AxisDisplayOf(item, axis.Id);
        }

        return buckets.Values
            .OrderByDescending(b => b.Count)
            .ThenBy(b => b.Value, StringComparer.OrdinalIgnoreCase)
            .Select(b => new CatalogGroupRow(axis.Id, axis.EffectiveDisplay, b.Value,
                CatalogText.Or(b.Display, b.Value), b.Count))
            .ToList();
    }

    private static string? AxisDisplayOf(CatalogItem item, string axisId)
        => item.AxisValueOf(axisId)?.EffectiveDisplay;

    /// <summary>分组累加桶（首见取值 + 计数 + 显示名）。</summary>
    private sealed class Bucket(string value)
    {
        public string Value { get; } = value;
        public int Count { get; set; }
        public string? Display { get; set; }
    }
}
