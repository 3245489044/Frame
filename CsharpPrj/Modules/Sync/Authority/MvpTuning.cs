namespace Modules.Sync.Authority;

/// <summary>
/// MVP 玩法数值的唯一声明点。
/// <para>
/// <b>全部为 provisional 占位值，来源：设计文档附录 C 待定项；不是平衡结论。</b>
/// 这些数值只用于让权威端在双端闭环里“跑起来并可观察”，任何平衡/胜率推断都不得引用本文件。
/// </para>
/// <para>
/// 契约寄存器里的冻结常量（<c>SnapshotHpScale</c> / <c>AtbScale</c> / <c>SkillSlots</c> / 频率）不在此声明，
/// 由宿主从契约注入到 <see cref="AuthorityTickLoop"/>。
/// </para>
/// </summary>
public static class MvpTuning
{
    /// <summary>provisional（附录 C #1）：同步预算上限 <c>S_max</c>。</summary>
    public const int SyncMax = 100;

    /// <summary>provisional（附录 C #1）：每个决策 tick 的基础恢复率 <c>r</c>。</summary>
    public const int SyncRegenPerDecisionTick = 8;

    /// <summary>provisional（附录 C #1）：规则 1「敌前摇 → 保护」的消耗 <c>c₁</c>（取值对齐设计文档 §6.3 示例）。</summary>
    public const int RuleCostGuard = 20;

    /// <summary>provisional（附录 C #1）：规则 2「低血 + 撤离激活 → 后撤」的消耗 <c>c₂</c>（对齐 §6.3 示例）。</summary>
    public const int RuleCostRetreat = 8;

    /// <summary>provisional（附录 C #1）：规则 3「ATB 满 → 技能」的消耗 <c>c₃</c>（对齐 §6.3 示例）。</summary>
    public const int RuleCostSkill = 12;

    /// <summary>provisional（附录 C #1）：手动「预判保护」消耗 <c>c_mut</c>。</summary>
    public const int ManualGuardCost = 24;

    /// <summary>provisional（附录 C #1）：手动「超频」消耗 <c>c_mut</c>。</summary>
    public const int ManualOverchargeCost = 30;

    /// <summary>provisional（附录 C #2）：手动超频额外消耗的 PP（超出技能本身的 1 点）。</summary>
    public const int OverchargeExtraPp = 2;

    /// <summary>provisional（附录 C #2）：每槽 PP 上限。</summary>
    public const int PpPerSlotMax = 3;

    /// <summary>provisional（附录 C #2）：每槽 PP 的被动回复周期（决策 tick）。</summary>
    public const int PpRegenIntervalTicks = 30;

    /// <summary>provisional（附录 C #2）：每槽每次回复的 PP 点数。</summary>
    public const int PpRegenPerSlot = 1;

    /// <summary>provisional（附录 C #2）：每个决策 tick 的 ATB 充能量（速度属性的最小占位表达）。</summary>
    public const int AtbChargePerDecisionTick = 40;

    /// <summary>provisional（附录 C #3）：保护窗口长度（0.5s = 5 个决策 tick，含首尾）。</summary>
    public const int GuardWindowTicks = 5;

    /// <summary>provisional（附录 C #3）：手动保护猜错后的硬直长度（1.2s = 12 个决策 tick）。</summary>
    public const int GuardMissStunTicks = 12;

    /// <summary>provisional（附录 C #1）：保护猜中免伤后回补的少量 Sync。</summary>
    public const int GuardHitSyncRefund = 6;

    /// <summary>provisional（附录 C #1）：规则 2 的 HP 阈值（千分制 0.3 → 300）。</summary>
    public const int RetreatHpThreshold = 300;

    /// <summary>provisional（附录 C #1）：后撤的保持时长（决策 tick），期间不再触发规则 3 的技能。</summary>
    public const int RetreatHoldTicks = 6;

    /// <summary>provisional（附录 C #6）：脚本化敌人的单次伤害。</summary>
    public const int EnemyAttackDamage = 180;

    /// <summary>provisional（附录 C #6）：脚本化敌人攻击周期（决策 tick）。</summary>
    public const int EnemyAttackIntervalTicks = 12;

    /// <summary>provisional（附录 C #6）：脚本化敌人前摇长度（决策 tick）；前摇首 tick 起可见，结束后立刻结算。</summary>
    public const int EnemyWindupTicks = 4;
}
