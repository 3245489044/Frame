#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FrontEndDriven.HtmlHosting;

/// <summary>取数成本档（界判据见领域声明面；本层只承载枚举）。</summary>
public enum UiCostClass
{
    Low = 0,
    High = 1,
}

/// <summary>一个数据需求：域 + 作用域 + 成本档 + 是否可跨拍复用。</summary>
public readonly record struct UiDataNeed(
    string Domain,
    string Scope,
    UiCostClass Cost = UiCostClass.Low,
    bool Reusable = false);

/// <summary>视图声明：需要哪些域、产出哪些区域（执行器据此取数，不再写分支梯）。</summary>
public sealed record UiViewSpec(
    string View,
    IReadOnlyList<UiDataNeed> Needs,
    IReadOnlyList<string> Sections);

/// <summary>数据源端口：按需求取数（域内实现，执行器不感知域）。</summary>
public interface IUiViewDataSource
{
    Task<object?> GatherAsync(UiDataNeed need, CancellationToken ct);
}

/// <summary>一拍的取数结果：域值表 + 实际取数域 + 复用域（可判定：复用了哪些域）。</summary>
public sealed record UiViewGatherResult(
    IReadOnlyDictionary<string, object?> Values,
    IReadOnlyList<string> Gathered,
    IReadOnlyList<string> Reused);

/// <summary>
/// 视图投影执行器（通用件，唯一实现）：<c>ChangeScope → DataNeeds</c> 取数，
/// 声明为 <see cref="UiDataNeed.Reusable"/> 且作用域未变更的域复用上拍快照（未命中则照常取数）。
/// <para>未变更域 0 取数、0 写目标由「复用域」清单可复算；新增数据域只加声明，不改执行器。</para>
/// </summary>
public sealed class UiViewProjection
{
    private readonly Dictionary<string, object?> _snapshot = new(StringComparer.Ordinal);

    /// <summary>执行一拍取数；<paramref name="changedScopes"/> 为 null ⇒ 视为全量变更（禁静默跳过）。</summary>
    public async Task<UiViewGatherResult> GatherAsync(
        UiViewSpec spec,
        IUiViewDataSource source,
        IReadOnlyCollection<string>? changedScopes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(source);

        var changed = changedScopes is null
            ? null
            : new HashSet<string>(changedScopes, StringComparer.OrdinalIgnoreCase);

        var values = new Dictionary<string, object?>(spec.Needs.Count, StringComparer.Ordinal);
        var gathered = new List<string>(spec.Needs.Count);
        var reused = new List<string>();

        foreach (var need in spec.Needs)
        {
            if (string.IsNullOrWhiteSpace(need.Domain)) continue;

            var unchanged = changed is not null && !changed.Contains(need.Scope);
            if (need.Reusable && unchanged && _snapshot.TryGetValue(need.Domain, out var cached))
            {
                values[need.Domain] = cached;
                reused.Add(need.Domain);
                continue;
            }

            var value = await source.GatherAsync(need, ct).ConfigureAwait(false);
            values[need.Domain] = value;
            _snapshot[need.Domain] = value;
            gathered.Add(need.Domain);
        }

        return new UiViewGatherResult(values, gathered, reused);
    }

    /// <summary>丢弃上拍快照（作用域切换/强制刷新时调用）。</summary>
    public void Reset() => _snapshot.Clear();
}
