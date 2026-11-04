#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using TConfig;

namespace UiBackend.Html;

/// <summary>
/// 区域目录（B4）：页面区域声明的唯一运行时索引 —— 写回目标 <c>region:&lt;key&gt;</c> 的合法域。
/// <para>未声明区域写入 = 装配/写回失败（禁静默丢弃）；<see cref="Default"/> 保留旧页面的区域键集合，
/// 供未声明 regions 的存量页面零回归。</para>
/// </summary>
public sealed class RegionCatalog
{
    public const string Prefix = "region:";

    private readonly Dictionary<string, HtmlPageRegionDeclaration> _regions;

    public RegionCatalog(IEnumerable<HtmlPageRegionDeclaration>? regions)
    {
        _regions = new Dictionary<string, HtmlPageRegionDeclaration>(StringComparer.OrdinalIgnoreCase);
        if (regions == null) return;

        foreach (var region in regions)
        {
            if (region == null || string.IsNullOrWhiteSpace(region.Key)) continue;
            _regions[region.Key] = region;
        }
    }

    /// <summary>页面声明的区域（缺省空集）。</summary>
    public static RegionCatalog From(HtmlPageDeclaration? page)
        => new(page?.Regions);

    /// <summary>声明的区域键快照。</summary>
    public IReadOnlyCollection<string> Keys => _regions.Keys.ToList();

    /// <summary>是否为已声明区域。</summary>
    public bool IsDeclared(string? key)
        => !string.IsNullOrWhiteSpace(key) && _regions.ContainsKey(Normalize(key!));

    /// <summary>取区域声明；未声明 → null。</summary>
    public HtmlPageRegionDeclaration? Resolve(string? key)
        => string.IsNullOrWhiteSpace(key) ? null : _regions.GetValueOrDefault(Normalize(key!));

    /// <summary>是否为区域目标（前缀判定，不校验声明）。</summary>
    public static bool IsRegionTarget(string? target)
        => !string.IsNullOrWhiteSpace(target)
           && target!.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>区域目标 → 区域键（非区域目标抛错）。</summary>
    public static string Normalize(string target)
        => target.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            ? target.Substring(Prefix.Length)
            : target;
}
