using System;
using System.Collections.Generic;
using System.Linq;
using Strategy.Catalog.Model;
using Strategy.Catalog.Ports;

namespace Strategy.Catalog.Core;

/// <summary>
/// 委托式目录源（**零新类接入**）—— 已有平铺集合（工具面 / 函数库 / 模板清单 / 产出链 …）
/// 只需给两段委托即可接入分级查询，不必为每个域写一个 <c>XxxCatalogSource</c> 类。
/// <para>明细委托缺省 = 由条目摘要退化产出一行明细（"没有更细信息"是合法状态，不抛）。</para>
/// </summary>
public sealed class DelegateCatalogSource : ICatalogSource
{
    private readonly Func<IReadOnlyList<CatalogItem>> _enumerate;
    private readonly Func<string, CatalogDetail?>? _describe;

    /// <param name="sourceId">目录源 Id（装配期唯一）。</param>
    /// <param name="display">显示名。</param>
    /// <param name="axes">声明的分类轴。</param>
    /// <param name="enumerate">条目枚举委托（每次查询调用；应为只读、无副作用）。</param>
    /// <param name="describe">明细委托（可空 = 退化摘要明细）。</param>
    public DelegateCatalogSource(
        string sourceId,
        string display,
        IReadOnlyList<CatalogAxis> axes,
        Func<IReadOnlyList<CatalogItem>> enumerate,
        Func<string, CatalogDetail?>? describe = null)
    {
        SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
        Display = string.IsNullOrWhiteSpace(display) ? sourceId : display;
        Axes = axes ?? Array.Empty<CatalogAxis>();
        _enumerate = enumerate ?? throw new ArgumentNullException(nameof(enumerate));
        _describe = describe;
    }

    /// <inheritdoc />
    public string SourceId { get; }

    /// <inheritdoc />
    public string Display { get; }

    /// <inheritdoc />
    public IReadOnlyList<CatalogAxis> Axes { get; }

    /// <inheritdoc />
    public IReadOnlyList<CatalogItem> Enumerate() => _enumerate() ?? Array.Empty<CatalogItem>();

    /// <inheritdoc />
    public CatalogDetail? Describe(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (_describe is not null) return _describe(key!);

        var item = Enumerate().FirstOrDefault(i => i is not null
            && string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase));
        return item is null
            ? null
            : CatalogDetail.Of(item.Key, item.EffectiveDisplay,
                new[]
                {
                    new CatalogField("键", item.Key),
                    new CatalogField("执行体轨", item.InvokeRef),
                    new CatalogField("轴", string.Join(", ",
                        (item.Axes ?? Array.Empty<CatalogAxisValue>())
                            .Select(a => $"{a.AxisId}={a.EffectiveDisplay}"))),
                },
                item.Summary, item.InvokeRef);
    }
}

/// <summary>
/// 平铺清单目录源（最常用形态的**语法糖**）—— 用「键 + 显示名 + 前缀轴自动派生」一行接入。
/// <para>
/// 典型用法（对齐既有 <c>GetDomains/GetFunctions</c> 的语义，但分组不再各写一遍）：
/// <code>
/// var source = ListCatalogSource.ByPrefix("functions", "函数库",
///     registry.GetRegisteredNames().Select(n => (n, (string?)null)));
/// </code>
/// </para>
/// </summary>
public static class ListCatalogSource
{
    /// <summary>前缀轴默认 Id。</summary>
    public const string PrefixAxisId = "prefix";

    /// <summary>前缀轴默认显示名。</summary>
    public const string PrefixAxisDisplay = "域";

    /// <summary>目录轴默认 Id。</summary>
    public const string FolderAxisId = "folder";

    /// <summary>目录轴默认显示名。</summary>
    public const string FolderAxisDisplay = "分类";

    /// <summary>按「首个下划线前缀」建轴：条目（键 + 摘要 + 执行体轨 + 常驻标记）→ 目录源。</summary>
    public static ICatalogSource ByPrefix(
        string sourceId,
        string display,
        IEnumerable<(string Key, string? Summary)> items,
        string axisId = PrefixAxisId,
        string axisDisplay = PrefixAxisDisplay)
        => new DelegateCatalogSource(sourceId, display,
            new[] { CatalogAxis.Of(axisId, axisDisplay) },
            () => (items ?? Enumerable.Empty<(string, string?)>())
                .Where(i => !string.IsNullOrWhiteSpace(i.Key))
                .Select(i => new CatalogItem(i.Key, i.Key,
                    new[] { CatalogAxes.PrefixValue(i.Key, axisId) }, i.Summary))
                .ToList());

    /// <summary>按「相对路径首段（文件夹）」建轴：键 = 相对路径，显示名可另给。</summary>
    public static ICatalogSource ByFolder(
        string sourceId,
        string display,
        IEnumerable<(string RelativePath, string? Display, string? Summary)> items,
        string axisId = FolderAxisId,
        string axisDisplay = FolderAxisDisplay)
        => new DelegateCatalogSource(sourceId, display,
            new[] { CatalogAxis.Of(axisId, axisDisplay) },
            () => (items ?? Enumerable.Empty<(string, string?, string?)>())
                .Where(i => !string.IsNullOrWhiteSpace(i.RelativePath))
                .Select(i => new CatalogItem(i.RelativePath,
                    string.IsNullOrWhiteSpace(i.Display) ? i.RelativePath : i.Display!,
                    new[] { CatalogAxes.FolderValue(i.RelativePath, axisId) }, i.Summary))
                .ToList());

    /// <summary>
    /// 按「首个下划线前缀」建轴（**委托版**）—— 条目集**每次查询现取**。
    /// <para>
    /// 用于运行期可变清单（如热加载的函数注册表）：装配期快照会导致目录陈旧，
    /// 故此处必须用委托而非一次性 IEnumerable。
    /// </para>
    /// </summary>
    public static ICatalogSource ByPrefix(
        string sourceId,
        string display,
        Func<IEnumerable<(string Key, string? Summary)>> itemsProvider,
        string axisId = PrefixAxisId,
        string axisDisplay = PrefixAxisDisplay)
        => new DelegateCatalogSource(sourceId, display,
            new[] { CatalogAxis.Of(axisId, axisDisplay) },
            () => (itemsProvider() ?? Enumerable.Empty<(string, string?)>())
                .Where(i => !string.IsNullOrWhiteSpace(i.Key))
                .Select(i => new CatalogItem(i.Key, i.Key,
                    new[] { CatalogAxes.PrefixValue(i.Key, axisId) }, i.Summary))
                .ToList());

    /// <summary>按「相对路径首段（文件夹）」建轴（**委托版**）—— 条目集每次查询现取（可变目录树必用）。</summary>
    public static ICatalogSource ByFolder(
        string sourceId,
        string display,
        Func<IEnumerable<(string RelativePath, string? Display, string? Summary)>> itemsProvider,
        string axisId = FolderAxisId,
        string axisDisplay = FolderAxisDisplay)
        => new DelegateCatalogSource(sourceId, display,
            new[] { CatalogAxis.Of(axisId, axisDisplay) },
            () => (itemsProvider() ?? Enumerable.Empty<(string, string?, string?)>())
                .Where(i => !string.IsNullOrWhiteSpace(i.RelativePath))
                .Select(i => new CatalogItem(i.RelativePath,
                    string.IsNullOrWhiteSpace(i.Display) ? i.RelativePath : i.Display!,
                    new[] { CatalogAxes.FolderValue(i.RelativePath, axisId) }, i.Summary))
                .ToList());
}
