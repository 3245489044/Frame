using System;
using System.Collections.Generic;
using System.Linq;

namespace Strategy.Catalog.Model;

/// <summary>
/// 目录条目（摘要行）—— 平铺集合中的**一条**，在索引/目录视图里只暴露「键 + 显示名 + 一句话 + 执行体轨」。
/// <para>
/// 明细（参数/契约/示例）不在本记录内：经 <c>ICatalogSource.Describe</c>（→ <see cref="CatalogDetail"/>）
/// 按需取，落实「索引浅、明细按需」的有界量化纪律。
/// </para>
/// </summary>
/// <param name="Key">条目稳定键（目录内唯一；也是明细/调用地址）。</param>
/// <param name="Display">显示名（人类可读；空则回退 <see cref="Key"/>）。</param>
/// <param name="Axes">挂载的轴取值（多轴并存；空集合 = 不参与任何轴分组）。</param>
/// <param name="Summary">一句话摘要（进索引/目录；**必须短**）。</param>
/// <param name="InvokeRef">执行体轨/来源（如回环面轨名、函数库名；null = 未标注）。</param>
/// <param name="Pinned">是否常驻（核心/入口条目：在一级索引中平铺展示，不藏进轴分组）。</param>
/// <param name="Tags">自由标签（供检索加权/过滤；可空）。</param>
public sealed record CatalogItem(
    string Key,
    string Display,
    IReadOnlyList<CatalogAxisValue> Axes,
    string? Summary = null,
    string? InvokeRef = null,
    bool Pinned = false,
    IReadOnlyList<string>? Tags = null)
{
    /// <summary>显示名（空则回退 <see cref="Key"/>，统一空值兜底入口）。</summary>
    public string EffectiveDisplay => string.IsNullOrWhiteSpace(Display) ? Key : Display!;

    /// <summary>取指定轴的取值；未挂载返回 null（大小写不敏感）。</summary>
    public string? ValueOf(string axisId)
    {
        if (string.IsNullOrWhiteSpace(axisId)) return null;
        foreach (var axis in Axes ?? Array.Empty<CatalogAxisValue>())
        {
            if (axis is null) continue;
            if (string.Equals(axis.AxisId, axisId, StringComparison.OrdinalIgnoreCase)) return axis.Value;
        }
        return null;
    }

    /// <summary>条目是否命中指定轴的取值（大小写不敏感；空取值 = 不命中）。</summary>
    public bool OnAxis(string axisId, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var mine = ValueOf(axisId);
        return !string.IsNullOrWhiteSpace(mine)
               && string.Equals(mine, value, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// 明细字段（明细视图的一行：名 + 值）。框架不解释字段语义（由 source 决定呈现什么）。
/// </summary>
/// <param name="Name">字段名。</param>
/// <param name="Value">字段值（可空）。</param>
public sealed record CatalogField(string Name, string? Value);

/// <summary>
/// 条目明细（<see cref="CatalogTier.Detail"/>）—— 按需展开的完整信息，**不进一级索引**。
/// </summary>
/// <param name="Key">条目键（与 <see cref="CatalogItem.Key"/> 一致）。</param>
/// <param name="Display">显示名。</param>
/// <param name="Summary">一句话摘要（可空）。</param>
/// <param name="Fields">明细字段集（键值对；序 = 呈现序）。</param>
/// <param name="InvokeRef">执行体轨/来源（可空）。</param>
public sealed record CatalogDetail(
    string Key,
    string Display,
    IReadOnlyList<CatalogField> Fields,
    string? Summary = null,
    string? InvokeRef = null)
{
    /// <summary>空字段集兜底（避免调用方判空）。</summary>
    public IReadOnlyList<CatalogField> FieldsOrEmpty => Fields ?? Array.Empty<CatalogField>();

    /// <summary>取字段值（名不区分大小写；未命中返回 null）。</summary>
    public string? Field(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var field in FieldsOrEmpty)
        {
            if (field is null) continue;
            if (string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase)) return field.Value;
        }
        return null;
    }

    /// <summary>按名构造（字段集便捷）。</summary>
    public static CatalogDetail Of(string key, string? display, IEnumerable<CatalogField>? fields, string? summary = null, string? invokeRef = null)
        => new(key, string.IsNullOrWhiteSpace(display) ? key : display!, fields?.ToList() ?? (IReadOnlyList<CatalogField>)Array.Empty<CatalogField>(), summary, invokeRef);
}
