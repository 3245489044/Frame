using Strategy.Observability.Ports;

namespace Strategy.Observability.SpecHost;

/// <summary>
/// 端口实现登记表：<c>id → 工厂</c>（<c>id</c> = 标准条目的 <c>impl.id</c> = 端口键，机械同值）。
/// <para>
/// 纪律：① 重复 <c>id</c> 登记即抛（端口只有一个实现点）；② **每装配一实例、即用即弃**
/// （由 <see cref="SpecPortComposition.BuildRegistry"/> 现场创建）⇒ 多次 / 并行装配语义由结构保证，
/// 机制半无静态可变状态（旧静态表 + 静态幂等门 + 锁随之消亡）。
/// </para>
/// </summary>
public sealed class SpecPortRegistry
{
    private readonly Dictionary<string, Func<IPortInput, object?>> _implementations = new(StringComparer.Ordinal);
    private IReadOnlyList<string>? _portIds;

    /// <summary>登记端口实现（重复登记即抛：单一实现点）。</summary>
    public void Register(string id, Func<IPortInput, object?> invoke)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        PortGuard.Required(invoke, nameof(invoke));

        if (!_implementations.TryAdd(id, invoke))
            throw new InvalidOperationException($"[port:bind] 端口 '{id}' 的实现重复登记（单一实现点被破坏）");

        _portIds = null;
    }

    /// <summary>取端口实现；未登记 → false（装配期记为未绑定并 fail-safe 继续）。</summary>
    public bool TryGet(string id, out Func<IPortInput, object?> invoke)
        => _implementations.TryGetValue(id, out invoke!);

    /// <summary>已登记实现的端口键（Ordinal 升序；登记后失效重算的快照，守卫 / 诊断用）。</summary>
    public IReadOnlyList<string> PortIds
        => _portIds ??= _implementations.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
}
