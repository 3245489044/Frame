namespace ContractFramework.Abstractions;

/// <summary>
/// 契约处置（Effect）——违例后的行为语义（文档 §5.2 处置矩阵）。
/// </summary>
public enum ContractEffect
{
    /// <summary>违例即异常（含修复引导）；适用 Build / Assembly，由 <see cref="Gate.ContractGate.Enforce"/> 单一抛出。</summary>
    FailFast = 0,

    /// <summary>阻断该动作，不阻断进程；返回 <see cref="ContractResult"/>（Bottom/False），调用方 fail-closed。</summary>
    Block = 1,

    /// <summary>触发受控收敛（重建/重算），计自愈预算；超预算 ⇒ 升级为日志告警（不停在不一致态）。</summary>
    Heal = 2,

    /// <summary>仅落盘 + 可见化（<see cref="Ledger.ContractLedger"/> 追加记录）。</summary>
    Log = 3,
}
