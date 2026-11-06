using System.Collections.Concurrent;
using TConfig;

namespace Operation.ReconcileDriven.Host;

/// <summary>
/// 对账契约登记表：绑定期按「函数名 → 契约」登记（来源 <see cref="EventBinding.Reconcile"/>），
/// 触发期按「函数名 + 命令选择器（payload.cmd）」解析。
/// <para>
/// 多路复用支持（G-1 闭合）：同一注册函数可并存多份契约 —— 命中 <c>when</c> 的命令级契约优先，
/// 未命中时回退到默认契约（<c>when</c> 为空者）。并发安全；同键重复登记以最后一次为准。
/// </para>
/// </summary>
public sealed class ReconcileContractRegistry
{
    private readonly ConcurrentDictionary<string, List<ReconcileConfig>> _byFunction = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    /// <summary>登记某函数的对账契约（同函数同 <c>when</c> 覆盖，不同 <c>when</c> 并存）。</summary>
    public void Register(string functionName, ReconcileConfig contract)
    {
        if (string.IsNullOrWhiteSpace(functionName) || contract is null) return;
        var when = string.IsNullOrWhiteSpace(contract.When) ? null : contract.When!.Trim();

        lock (_gate)
        {
            var list = _byFunction.GetOrAdd(functionName, _ => new List<ReconcileConfig>());
            var existing = list.FindIndex(c => string.Equals(
                string.IsNullOrWhiteSpace(c.When) ? null : c.When!.Trim(), when, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0) list[existing] = contract;
            else list.Add(contract);
        }
    }

    /// <summary>按函数名解析默认契约（无 <c>when</c> 者）。</summary>
    public bool TryGet(string functionName, out ReconcileConfig? contract)
        => TryGet(functionName, null, out contract);

    /// <summary>
    /// 按「函数名 + 命令」解析契约：命令级契约（<c>when==cmd</c>）优先，未命中回退默认契约（无 <c>when</c>）。
    /// 两者皆无 ⇒ false（直通，零对账）。
    /// </summary>
    public bool TryGet(string functionName, string? command, out ReconcileConfig? contract)
    {
        contract = null;
        if (string.IsNullOrWhiteSpace(functionName)) return false;
        if (!_byFunction.TryGetValue(functionName, out var list)) return false;

        lock (_gate)
        {
            if (list.Count == 0) return false;

            if (!string.IsNullOrWhiteSpace(command))
            {
                var hit = list.FirstOrDefault(c => string.Equals(c.When?.Trim(), command!.Trim(), StringComparison.OrdinalIgnoreCase));
                if (hit is not null) { contract = hit; return true; }
            }

            var fallback = list.FirstOrDefault(c => string.IsNullOrWhiteSpace(c.When));
            if (fallback is not null) { contract = fallback; return true; }
            return false;
        }
    }

    /// <summary>已登记函数数。</summary>
    public int Count => _byFunction.Count;

    /// <summary>清空（测试/换代用）。</summary>
    public void Clear()
    {
        lock (_gate) _byFunction.Clear();
    }
}
