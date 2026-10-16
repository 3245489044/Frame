using System.Collections.Concurrent;
using TConfig;

namespace Operation.ReconcileDriven.Host;

/// <summary>
/// 对账契约登记表：绑定期按「函数名 → 契约」登记（来源 <see cref="EventBinding.Reconcile"/>），
/// 触发期按函数名解析。并发安全；重复登记以最后一次为准。
/// </summary>
public sealed class ReconcileContractRegistry
{
    private readonly ConcurrentDictionary<string, ReconcileConfig> _byFunction = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>登记某函数的对账契约。</summary>
    public void Register(string functionName, ReconcileConfig contract)
    {
        if (string.IsNullOrWhiteSpace(functionName) || contract is null) return;
        _byFunction[functionName] = contract;
    }

    /// <summary>按函数名解析契约。</summary>
    public bool TryGet(string functionName, out ReconcileConfig? contract)
        => _byFunction.TryGetValue(functionName ?? string.Empty, out contract);

    /// <summary>已登记条数。</summary>
    public int Count => _byFunction.Count;

    /// <summary>清空（测试/换代用）。</summary>
    public void Clear() => _byFunction.Clear();
}
