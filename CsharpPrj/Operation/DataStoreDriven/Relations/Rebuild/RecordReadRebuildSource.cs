using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;

namespace Operation.DataStoreDriven.Relations.Rebuild;

/// <summary>
/// 行读端口 → 重建源（域 + 有界上限；不可映射行显式回报，禁静默丢弃）。
/// <para>领域差异只以 <c>map</c> 委托出现（框架不解析领域载荷格式）。</para>
/// </summary>
public sealed class RecordReadRebuildSource<T> : IRebuildSource<T>
{
    private readonly IRecordReadPort _records;
    private readonly string _domain;
    private readonly int _limit;
    private readonly Func<RecordView, T?> _map;
    private readonly Action<RecordView, string>? _onSkipped;

    public RecordReadRebuildSource(
        IRecordReadPort records,
        string domain,
        Func<RecordView, T?> map,
        int limit = 10000,
        Action<RecordView, string>? onSkipped = null)
    {
        _records = records ?? throw new ArgumentNullException(nameof(records));
        if (string.IsNullOrWhiteSpace(domain)) throw new ArgumentException("域键不能为空", nameof(domain));
        _map = map ?? throw new ArgumentNullException(nameof(map));
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit), "limit 须为正整数（有界量化）");

        _domain = domain;
        _limit = limit;
        _onSkipped = onSkipped;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<T> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var views = await _records.ReadManyAsync(_domain, null, _limit, ct).ConfigureAwait(false);
        foreach (var view in views)
        {
            ct.ThrowIfCancellationRequested();

            T? row;
            try
            {
                row = _map(view);
            }
            catch (Exception ex)
            {
                _onSkipped?.Invoke(view, "map-failed:" + ex.Message);
                continue;
            }

            if (row is null)
            {
                _onSkipped?.Invoke(view, "unmappable-row");
                continue;
            }

            yield return row;
        }
    }
}

/// <summary>组合投影：同一行 → 多类动作（顺序 = 构造顺序；禁在此做分支编排）。</summary>
public sealed class CompositeRebuildProjection<T> : IRebuildProjection<T>
{
    private readonly IReadOnlyList<IRebuildProjection<T>> _projections;

    public CompositeRebuildProjection(params IRebuildProjection<T>[] projections)
    {
        ArgumentNullException.ThrowIfNull(projections);
        if (projections.Length == 0) throw new ArgumentException("至少需要一个投影", nameof(projections));
        _projections = projections;
    }

    /// <inheritdoc />
    public IEnumerable<RebuildAction> Project(T item)
    {
        foreach (var projection in _projections)
        {
            foreach (var action in projection.Project(item))
            {
                yield return action;
            }
        }
    }
}
