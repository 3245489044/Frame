namespace ContractFramework.Abstractions;

/// <summary>
/// 三值裁决（对齐 TruthValueDomainLogic）：<c>T</c> 成立放行 / <c>F</c> 违例 / <c>⊥</c> 不可判定（不放行）。
/// <para>关键：<c>⊥ ≠ T</c>。判据不全默认放行是本框架要消灭的缺陷；本框架把 <c>⊥</c> 显式化为拒放行 + 可见告警。</para>
/// </summary>
public enum ContractVerdict
{
    /// <summary>契约成立 → 放行。</summary>
    True = 0,

    /// <summary>违例（有结构事实证据）→ 按 <see cref="ContractEffect"/> 处置。</summary>
    False = 1,

    /// <summary>不可判定（载体不合格 / 证据缺失 / 未注册执行体）→ fail-closed（不放行）。</summary>
    Bottom = 2,
}

/// <summary>三值裁决操作（合取 + 放行判据的极小实现；载体 = 判据层级真值域）。</summary>
public static class ContractVerdictLogic
{
    /// <summary>合取（Scott 域：⊥ 吸收 <c>⊥∧x=⊥</c>；F 吸收 T <c>F∧x=F</c>）。</summary>
    public static ContractVerdict Conjunction(ContractVerdict left, ContractVerdict right)
    {
        if (left == ContractVerdict.Bottom || right == ContractVerdict.Bottom)
        {
            return ContractVerdict.Bottom;
        }

        if (left == ContractVerdict.False || right == ContractVerdict.False)
        {
            return ContractVerdict.False;
        }

        return ContractVerdict.True;
    }

    /// <summary>仅 <c>T</c> 放行；<c>F</c>/<c>⊥</c> 一律不放行（<c>⊥</c> 恒 fail-closed）。</summary>
    public static bool IsReleased(ContractVerdict verdict) => verdict == ContractVerdict.True;
}
