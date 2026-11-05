using System;
using System.Collections.Concurrent;

namespace Modules.Node.Stream;

/// <summary>
/// 聚合帧容器的**取用门面**（泛型；每域一实例）：承载经注入的 <see cref="IPartitionSlots{TRun}"/>
/// 按分区键隔离（**禁**进程级单值承载运行中帧）。
/// <para>槽只增不减 ⇒ <see cref="Release"/> 以「清空内容 + 复位轮次」表达释放。差异参数 = 槽端口 + 运行容器工厂。</para>
/// </summary>
/// <typeparam name="TRun">运行容器类型（该域的 <see cref="AggregateRun"/> 子类）。</typeparam>
public sealed class AggregateRegistry<TRun>
    where TRun : AggregateRun
{
    private readonly IPartitionSlots<TRun> _slots;
    private readonly Func<string, TRun> _factory;

    // 容器键规范登记（与过程面同源口径：trim + 大小写不敏感归并到首见规范键）。
    private readonly ConcurrentDictionary<string, string> _canonicalKeys =
        new(StringComparer.OrdinalIgnoreCase);

    /// <param name="slots">分区槽端口（实例级：内容随 run 实例变化）。</param>
    /// <param name="factory">运行容器工厂（分区键 → 容器）。</param>
    public AggregateRegistry(IPartitionSlots<TRun> slots, Func<string, TRun> factory)
    {
        _slots = slots ?? throw new ArgumentNullException(nameof(slots));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>容器键**唯一规范入口**（trim + 大小写不敏感归并）。</summary>
    /// <param name="partitionKey">原始分区键。</param>
    public string Canonical(string? partitionKey)
    {
        var normalized = NodeContainerKeys.Normalize(partitionKey);
        return normalized.Length == 0 ? string.Empty : _canonicalKeys.GetOrAdd(normalized, normalized);
    }

    /// <summary>取（缺失则装配）该分区的容器。</summary>
    /// <param name="partitionKey">分区键。</param>
    public TRun For(string partitionKey)
    {
        var key = Canonical(partitionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _slots.GetOrAdd(key, _factory);
    }

    /// <summary>进入轮次（循环边界唯一入口）：建容器（幂等）+ 置当前轮次。</summary>
    /// <param name="partitionKey">分区键。</param>
    /// <param name="round">轮次号。</param>
    public TRun Begin(string partitionKey, int round)
    {
        var run = For(partitionKey);
        run.BeginRound(round);
        return run;
    }

    /// <summary>尝试取该分区的容器（未装配过 ⇒ false，**不装配**：避免读路径隐式造容器）。</summary>
    /// <param name="partitionKey">分区键。</param>
    /// <param name="run">容器（未命中为 null）。</param>
    public bool TryGet(string partitionKey, out TRun? run)
    {
        run = null;
        var key = Canonical(partitionKey);
        if (key.Length == 0)
        {
            return false;
        }

        return _slots.TryGet(key, out run);
    }

    /// <summary>释放该分区的内存帧（落库成功后调用；容器本身留槽，内容清空）。</summary>
    /// <param name="partitionKey">分区键。</param>
    public void Release(string partitionKey)
    {
        if (TryGet(partitionKey, out var run))
        {
            run!.Release();
        }
    }
}

/// <summary>
/// Stream 形态（<c>PlacementForm.Stream</c>）域运行时的**域无关门面**：把泛型
/// <see cref="AggregateRegistry{TRun}"/> 收敛为可存入「域键 → 运行时」分派表的统一接口。
/// </summary>
public interface IStreamRegistry
{
    /// <summary>容器键**唯一规范入口**（trim + 大小写不敏感归并）。</summary>
    string Canonical(string? partitionKey);

    /// <summary>进入轮次（建容器幂等 + 置当前轮次）。</summary>
    AggregateRun Begin(string partitionKey, int round);

    /// <summary>尝试取该分区的容器（未装配 ⇒ false，**不装配**）。</summary>
    bool TryGet(string partitionKey, out AggregateRun? run);

    /// <summary>释放该分区的内存帧（容器留槽，内容清空）。</summary>
    void Release(string partitionKey);
}

/// <summary>
/// <see cref="IStreamRegistry"/> 的泛型适配：把具体 <typeparamref name="TRun"/> 的注册表暴露为统一接口，
/// 返回类型上溯到基类 <see cref="AggregateRun"/>，供异构域共用一张分派表。
/// </summary>
/// <typeparam name="TRun">运行容器类型（该域的 <see cref="AggregateRun"/> 子类）。</typeparam>
public sealed class AggregateStreamRegistry<TRun> : IStreamRegistry
    where TRun : AggregateRun
{
    private readonly AggregateRegistry<TRun> _registry;

    /// <param name="registry">该域的通用注册表实例。</param>
    public AggregateStreamRegistry(AggregateRegistry<TRun> registry)
        => _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    /// <inheritdoc />
    public string Canonical(string? partitionKey) => _registry.Canonical(partitionKey);

    /// <inheritdoc />
    public AggregateRun Begin(string partitionKey, int round) => _registry.Begin(partitionKey, round);

    /// <inheritdoc />
    public bool TryGet(string partitionKey, out AggregateRun? run)
    {
        var found = _registry.TryGet(partitionKey, out var typed);
        run = typed;
        return found;
    }

    /// <inheritdoc />
    public void Release(string partitionKey) => _registry.Release(partitionKey);
}
