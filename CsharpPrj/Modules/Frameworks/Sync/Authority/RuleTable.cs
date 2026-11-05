namespace Modules.Sync.Authority;

/// <summary>
/// 协议槽身份。序号即**固定短路顺序**：0 先于 1 先于 2；序号一旦命中即停止，后面的槽不再求值。
/// <para>这是 MVP 写死的规则表身份，不是可热更的 DSL（DSL 在 MVP 之外）。</para>
/// </summary>
public enum RuleId : byte
{
    /// <summary>槽 0：敌前摇可见 → 保护。</summary>
    TelegraphGuard = 0,

    /// <summary>槽 1：自身血量阈值 + 撤离点激活 → 后撤。</summary>
    LowHpRetreat = 1,

    /// <summary>槽 2：ATB 满 + 有可用 PP → 释放技能。</summary>
    AtbSkill = 2,
}

/// <summary>动作词表（MVP 只实现这三个动作；精准射击/热修/闪避等明确排除）。</summary>
public enum RuleAction : byte
{
    /// <summary>未命中任何规则。</summary>
    None = 0,

    /// <summary>进入保护协议（开保护窗口）。</summary>
    Guard = 1,

    /// <summary>后撤到掩体（消耗 Sync 并进入后撤保持）。</summary>
    Retreat = 2,

    /// <summary>释放技能（扣 PP、清 ATB）。</summary>
    Skill = 3,
}

/// <summary>一条协议槽：(身份, 动作, 消耗) 三元组。</summary>
public readonly struct RuleEntry
{
    /// <summary>构造一条规则槽。</summary>
    public RuleEntry(RuleId id, RuleAction action, int cost)
    {
        Id = id;
        Action = action;
        Cost = cost;
    }

    /// <summary>槽身份（决定短路顺序）。</summary>
    public RuleId Id { get; }

    /// <summary>命中的动作。</summary>
    public RuleAction Action { get; }

    /// <summary>动作消耗 <c>cᵢ</c>（命中的前提之一，不足则不命中）。</summary>
    public int Cost { get; }

    /// <summary>该槽的固定序号。</summary>
    public int Index => (int)Id;
}

/// <summary>
/// 固定 3 条协议规则表（写死，容量不做无界遍历）。
/// <para>求值契约：<c>for i in 0..min(ruleLimit, 3) - 1</c>，自上而下；第一条条件为真者执行并立即停止。</para>
/// </summary>
public static class RuleTable
{
    private static readonly RuleEntry[] Rules =
    {
        new RuleEntry(RuleId.TelegraphGuard, RuleAction.Guard, MvpTuning.RuleCostGuard),
        new RuleEntry(RuleId.LowHpRetreat, RuleAction.Retreat, MvpTuning.RuleCostRetreat),
        new RuleEntry(RuleId.AtbSkill, RuleAction.Skill, MvpTuning.RuleCostSkill),
    };

    /// <summary>MVP 规则表容量（写死 3，供宿主做 ruleCount sweep 的上界）。</summary>
    public static int Count => Rules.Length;

    /// <summary>按短路顺序取第 <paramref name="index"/> 条。</summary>
    public static RuleEntry At(int index)
    {
        if ((uint)index >= (uint)Rules.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "规则槽越界。");
        }

        return Rules[index];
    }
}
