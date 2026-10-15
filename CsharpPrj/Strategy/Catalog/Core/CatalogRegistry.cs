using System;
using System.Collections.Generic;
using System.Linq;
using Strategy.Catalog.Diagnostics;
using Strategy.Catalog.Ports;

namespace Strategy.Catalog.Core;

/// <summary>
/// 目录源注册表（**唯一装配点**）—— 各域把自己的 <see cref="ICatalogSource"/> 注册进来，
/// 查询统一走 <see cref="CatalogQueryService"/>。
/// <para>
/// 纪律：同一 Id 注册**不同实例** → 抛（装配期显式失败，杜绝"后注册静默覆盖先注册"）；
/// 同 Id 同实例重复注册 → 幂等忽略（便于组合根多处装配）。替换走 <see cref="Replace"/>。
/// </para>
/// </summary>
public sealed class CatalogRegistry : ICatalogSourceRegistry
{
    /// <summary>进程默认注册表（宿主自建实例时可不使用；测试用 <see cref="Clear"/> 隔离）。</summary>
    public static CatalogRegistry Default { get; } = new();

    private readonly Dictionary<string, ICatalogSource> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ICatalogSource> _ordered = new();

    /// <inheritdoc />
    public IReadOnlyList<ICatalogSource> Sources => _ordered;

    /// <summary>已注册源数。</summary>
    public int Count => _ordered.Count;

    /// <summary>注册一个目录源（空 Id 抛；重复 Id 见类注释纪律）。</summary>
    public void Register(ICatalogSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(source.SourceId))
            throw new ArgumentException("目录源 SourceId 不可为空。", nameof(source));

        if (_byId.TryGetValue(source.SourceId, out var existing))
        {
            if (ReferenceEquals(existing, source)) return; // 幂等
            throw new DuplicateCatalogSourceException(source.SourceId);
        }

        _byId[source.SourceId] = source;
        _ordered.Add(source);
    }

    /// <summary>批量注册（序 = 传入序）。</summary>
    public void RegisterAll(IEnumerable<ICatalogSource>? sources)
    {
        foreach (var source in sources ?? Enumerable.Empty<ICatalogSource>())
        {
            if (source is null) continue;
            Register(source);
        }
    }

    /// <summary>显式替换（装配期热换源；不存在则等价 <see cref="Register"/>）。</summary>
    public void Replace(ICatalogSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(source.SourceId))
            throw new ArgumentException("目录源 SourceId 不可为空。", nameof(source));

        if (_byId.TryGetValue(source.SourceId, out var existing))
        {
            var index = _ordered.IndexOf(existing);
            if (index >= 0) _ordered[index] = source;
            _byId[source.SourceId] = source;
            return;
        }

        Register(source);
    }

    /// <summary>清空（测试隔离：使装配态可复现）。</summary>
    public void Clear()
    {
        _byId.Clear();
        _ordered.Clear();
    }

    /// <inheritdoc />
    public ICatalogSource? Resolve(string? sourceId)
        => !string.IsNullOrWhiteSpace(sourceId) && _byId.TryGetValue(sourceId!, out var source) ? source : null;

    /// <inheritdoc />
    public bool Contains(string? sourceId)
        => !string.IsNullOrWhiteSpace(sourceId) && _byId.ContainsKey(sourceId!);

    /// <summary>按 Id 解析源；未注册抛（调用边界显式失败，带出已注册清单供自纠）。</summary>
    public ICatalogSource Require(string? sourceId)
        => Resolve(sourceId)
           ?? throw new UnknownCatalogSourceException(sourceId ?? string.Empty,
               _ordered.Select(s => s.SourceId).ToList());
}
