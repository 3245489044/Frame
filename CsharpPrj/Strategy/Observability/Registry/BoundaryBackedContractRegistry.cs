using Strategy.Observability.Boundary;

namespace Strategy.Observability.Registry;

/// <summary>
/// 兼容契约注册表（S2）：以 <see cref="BoundaryContract"/> 为唯一事实源，
/// 对既有 <see cref="IObservabilityContractRegistry"/> 消费者保持零改动——
/// 旧入口（<see cref="Register"/>）与语义文件（<see cref="ObservabilitySemanticsJson"/>）均投影为边界契约后登记。
/// </summary>
public sealed class BoundaryBackedContractRegistry : IObservabilityContractRegistry
{
    private readonly IBoundaryContractRegistry _boundaries;
    private readonly ObservabilityContractRegistry _validation = new();

    public BoundaryBackedContractRegistry(IBoundaryContractRegistry boundaries)
    {
        _boundaries = boundaries ?? throw new ArgumentNullException(nameof(boundaries));
    }

    /// <summary>兼容入口：事件契约投影为边界契约后登记（重复登记即抛）。</summary>
    public bool Register(EventContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (!_boundaries.TryRegister(contract.ToBoundaryContract())) return false;
        return _validation.Register(contract);
    }

    /// <summary>原生入口：直接登记事件类边界契约。</summary>
    public bool Register(BoundaryContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (!_boundaries.TryRegister(contract)) return false;
        return _validation.Register(contract.ToEventContract());
    }

    /// <summary>批量装载既有语义文件（observability_semantics.json）——迁移期零改动读取。</summary>
    public int RegisterRange(IEnumerable<EventContract> contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        var count = 0;
        foreach (var contract in contracts)
            if (Register(contract)) count++;
        return count;
    }

    /// <summary>校验委托给既有实现（E1–E8 语义不变）。</summary>
    public bool Validate(Envelope.ObservabilityEvent evt, out string? issue)
        => _validation.Validate(evt, out issue);

    public bool IsRegistered(string eventKind) => _validation.IsRegistered(eventKind);

    public IReadOnlyList<EventContract> Contracts => _validation.Contracts;
}
