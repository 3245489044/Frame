using System.Collections.Concurrent;
using Strategy.Observability.Diagnostics;

namespace Strategy.Observability.Boundary;

/// <summary>边界契约注册端口（E9）：键单一事实源，未登记即抛（杜绝未声明边界静默流通）。</summary>
public interface IBoundaryContractRegistry
{
    /// <summary>登记一条契约；同键重复登记抛 <see cref="DuplicateBoundaryException"/>。</summary>
    void Register(BoundaryContract contract);

    /// <summary>登记一条契约；同键已存在返回 false（不抛，供合并多来源播种）。</summary>
    bool TryRegister(BoundaryContract contract);

    /// <summary>批量登记（重复项仍按 <see cref="Register"/> 语义抛错）。</summary>
    void RegisterRange(IEnumerable<BoundaryContract> contracts);

    /// <summary>按键解析；未登记抛 <see cref="BoundaryNotRegisteredException"/>（含能力清单）。</summary>
    BoundaryContract Require(string key);

    bool TryGet(string key, out BoundaryContract contract);

    bool IsRegistered(string key);

    /// <summary>按键注销（登记失败补偿用；不存在返回 false）。</summary>
    bool TryRemove(string key);

    /// <summary>全部契约（按键 Ordinal 升序，保证确定性）。</summary>
    IReadOnlyList<BoundaryContract> Contracts { get; }

    /// <summary>端口类契约（按键 Ordinal 升序）。</summary>
    IReadOnlyList<BoundaryContract> Ports { get; }

    /// <summary>清空（测试隔离用）。</summary>
    void Clear();
}

/// <summary>内存契约注册表实现（E9）：并发安全；只登记不解释语义。</summary>
public sealed class BoundaryContractRegistry : IBoundaryContractRegistry
{
    private readonly ConcurrentDictionary<string, BoundaryContract> _byKey = new(StringComparer.Ordinal);

    public void Register(BoundaryContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        Validate(contract);
        if (!_byKey.TryAdd(contract.Key, contract))
            throw new DuplicateBoundaryException(contract.Key);
    }

    public bool TryRegister(BoundaryContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        Validate(contract);
        return _byKey.TryAdd(contract.Key, contract);
    }

    public void RegisterRange(IEnumerable<BoundaryContract> contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        foreach (var contract in contracts) Register(contract);
    }

    public BoundaryContract Require(string key)
        => TryGet(key, out var contract)
            ? contract
            : throw new BoundaryNotRegisteredException(key, Keys());

    public bool TryGet(string key, out BoundaryContract contract)
        => _byKey.TryGetValue(key, out contract!);

    public bool IsRegistered(string key) => _byKey.ContainsKey(key);

    public bool TryRemove(string key) => _byKey.TryRemove(key, out _);

    public IReadOnlyList<BoundaryContract> Contracts
        => _byKey.Values.OrderBy(c => c.Key, StringComparer.Ordinal).ToArray();

    public IReadOnlyList<BoundaryContract> Ports
        => _byKey.Values.Where(c => c.Kind == BoundaryKind.Port).OrderBy(c => c.Key, StringComparer.Ordinal).ToArray();

    public void Clear() => _byKey.Clear();

    /// <summary>契约自身的声明合法性（注册期 fail-fast，防声明漂移）。</summary>
    private static void Validate(BoundaryContract contract)
    {
        if (string.IsNullOrWhiteSpace(contract.Key))
            throw new BoundaryContractViolationException("(空键)", "契约键不能为空");

        var duplicated = contract.Fields
            .GroupBy(f => f.Name, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicated is not null)
            throw new BoundaryContractViolationException(contract.Key, $"字段 '{duplicated.Key}' 重复声明");

        var emptyField = contract.Fields.FirstOrDefault(f => string.IsNullOrWhiteSpace(f.Name));
        if (emptyField is not null)
            throw new BoundaryContractViolationException(contract.Key, "存在空字段名");

        if (contract.Kind == BoundaryKind.Port && contract.Depends.Any(d => string.IsNullOrWhiteSpace(d.PortKey)))
            throw new BoundaryContractViolationException(contract.Key, "存在空依赖端口键");
    }

    private IReadOnlyList<string> Keys() => _byKey.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
}
