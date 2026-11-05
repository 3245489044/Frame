using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Composition.AssemblyFramework;

/// <summary>
/// 声明面/消费面键集合（注册序保序；空白键拒绝）。
/// </summary>
public sealed class SurfaceSet
{
    private readonly List<string> _order = new();
    private readonly HashSet<string> _set;

    public SurfaceSet(string name, IEnumerable<string>? items = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("面名不能为空。", nameof(name));
        Name = name.Trim();
        _set = new HashSet<string>(StringComparer.Ordinal);
        if (items is null) return;
        foreach (var item in items) Add(item);
    }

    /// <summary>面名（诊断口径）。</summary>
    public string Name { get; }

    /// <summary>已登记键（注册序）。</summary>
    public IReadOnlyList<string> Items => _order;

    /// <summary>键数量。</summary>
    public int Count => _order.Count;

    public bool Contains(string? key) => !string.IsNullOrWhiteSpace(key) && _set.Contains(key.Trim());

    /// <summary>登记一个键（重复登记幂等；空白键拒不登记）。</summary>
    public bool Add(string? key)
    {
        var normalized = key?.Trim();
        if (string.IsNullOrEmpty(normalized) || !_set.Add(normalized)) return false;
        _order.Add(normalized);
        return true;
    }

    /// <summary>由序列构造（空序列 = 空面）。</summary>
    public static SurfaceSet Of(string name, IEnumerable<string>? items) => new(name, items);
}

/// <summary>两面差异（消费方有而生产方无 = 悬空消费；反向 = 未消费声明）。</summary>
public sealed record SurfaceDelta(
    string Consumer,
    string Producer,
    IReadOnlyList<string> MissingInProducer,
    IReadOnlyList<string> MissingInConsumer)
{
    /// <summary>是否无差异。</summary>
    public bool IsEmpty => MissingInProducer.Count == 0 && MissingInConsumer.Count == 0;
}

/// <summary>多面闭合报告（任一差异即不闭合）。</summary>
public sealed record SurfaceReport(IReadOnlyList<SurfaceDelta> Deltas)
{
    /// <summary>空报告（无面参与）。</summary>
    public static SurfaceReport Vacuous { get; } = new(Array.Empty<SurfaceDelta>());

    /// <summary>是否闭合（零差异）。</summary>
    public bool IsClosed => Deltas.Count == 0;

    /// <summary>差异逐条描述（诊断/门禁账本口径）。</summary>
    public IReadOnlyList<string> Describe()
        => Deltas.Select(d =>
                $"{d.Consumer} → {d.Producer}: 生产方缺 [{string.Join(", ", d.MissingInProducer)}]；消费方缺 [{string.Join(", ", d.MissingInConsumer)}]")
            .ToArray();
}

/// <summary>
/// 面闭合对账（通用件，零领域类型）：逐对比较两面键集合，差异即装配缺口。
/// <para>语义选项：<see cref="Compare"/> 双向相等；<see cref="CompareSubset"/> 只判「消费方 ⊆ 生产方」。</para>
/// </summary>
public static class SurfaceClosure
{
    /// <summary>双向集合对账（任一方向缺失均记差异）。</summary>
    public static SurfaceReport Compare(SurfaceSet consumer, SurfaceSet producer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        ArgumentNullException.ThrowIfNull(producer);

        var missingInProducer = consumer.Items.Where(k => !producer.Contains(k)).ToArray();
        var missingInConsumer = producer.Items.Where(k => !consumer.Contains(k)).ToArray();
        return missingInProducer.Length == 0 && missingInConsumer.Length == 0
            ? SurfaceReport.Vacuous
            : new SurfaceReport(new[] { new SurfaceDelta(consumer.Name, producer.Name, missingInProducer, missingInConsumer) });
    }

    /// <summary>子集对账：消费方每个键都必须在生产方出现（生产方多出的键不算差异）。</summary>
    public static SurfaceReport CompareSubset(SurfaceSet consumer, SurfaceSet producer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        ArgumentNullException.ThrowIfNull(producer);

        var missing = consumer.Items.Where(k => !producer.Contains(k)).ToArray();
        return missing.Length == 0
            ? SurfaceReport.Vacuous
            : new SurfaceReport(new[] { new SurfaceDelta(consumer.Name, producer.Name, missing, Array.Empty<string>()) });
    }

    /// <summary>批量双向对账（逐对取并集报告）。</summary>
    public static SurfaceReport CompareAll(IReadOnlyList<(SurfaceSet Consumer, SurfaceSet Producer)>? pairs)
    {
        if (pairs is null || pairs.Count == 0) return SurfaceReport.Vacuous;
        var deltas = new List<SurfaceDelta>();
        foreach (var (consumer, producer) in pairs)
            deltas.AddRange(Compare(consumer, producer).Deltas);
        return deltas.Count == 0 ? SurfaceReport.Vacuous : new SurfaceReport(deltas);
    }

    /// <summary>
    /// 装配期强制：报告不闭合 ⇒ 抛 <see cref="AssemblyIncompleteException"/>（逐面缺失原因）。
    /// </summary>
    public static void Assert(SurfaceReport report, string context)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.IsClosed) return;

        var slots = report.Deltas
            .Select(d => new SlotRequirement(
                $"{d.Consumer}→{d.Producer}",
                Present: false,
                d.MissingInProducer.Count == 0
                    ? $"消费方缺 [{string.Join(", ", d.MissingInConsumer)}]"
                    : $"生产方缺 [{string.Join(", ", d.MissingInProducer)}]"))
            .ToArray();

        throw new AssemblyIncompleteException(
            slots.Select(s => s.Slot).ToArray(),
            slots.Select(s => s.MissingReason ?? string.Empty).ToArray(),
            $"{context}：面闭合失败（{report.Deltas.Count} 处差异）。");
    }
}
