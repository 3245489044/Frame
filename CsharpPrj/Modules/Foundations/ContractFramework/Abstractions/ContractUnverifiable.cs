namespace ContractFramework.Abstractions;

/// <summary>
/// 不可判定条目（<c>⊥</c> 清单项）——载体不合格 / 证据缺失 / 未注册执行体等。
/// <para><c>⊥ ≠ T</c>：这些契约不参与放行（fail-closed），并显式可见化。</para>
/// </summary>
public sealed record ContractUnverifiable(ContractId ContractId, string Reason)
{
    public override string ToString() => $"{ContractId}: {Reason}";
}
