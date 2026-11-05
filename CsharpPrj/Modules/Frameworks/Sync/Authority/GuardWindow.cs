namespace Modules.Sync.Authority;

/// <summary>
/// 保护窗口（用 tick 表达，不用墙钟）。
/// <para>窗口语义：在第 <c>S</c> 个决策 tick 打开、长度 <c>W</c>，覆盖闭区间 <c>[S, S + W − 1]</c>。</para>
/// <para>
/// “猜中”= 敌方伤害结算 tick 落在覆盖区间内 → 免伤并回补少量 Sync；
/// “猜错”= 窗口到点关闭时仍未被任何结算命中，且该窗口标记了 <see cref="PenaltyOnMiss"/> → 进入硬直。
/// 自动协议开的窗口不罚硬直（设计里是“被骗后长冷却”，MVP 不做冷却）。
/// </para>
/// </summary>
public sealed class GuardWindow
{
    /// <summary>窗口是否处于打开状态。</summary>
    public bool IsOpen { get; private set; }

    /// <summary>窗口起始决策 tick（仅在 <see cref="IsOpen"/> 为 true 时有意义）。</summary>
    public long StartTick { get; private set; }

    /// <summary>窗口结束决策 tick（含），仅在打开时有意义。</summary>
    public long UntilTick { get; private set; }

    /// <summary>猜错时是否罚硬直（手动预判保护为 true）。</summary>
    public bool PenaltyOnMiss { get; private set; }

    /// <summary>窗口内是否至少命中过一次伤害结算。</summary>
    public bool Absorbed { get; private set; }

    /// <summary>对外可见的 <c>GuardUntilTick</c>：未打开时为 0（契约的“无窗口”哨兵）。</summary>
    public long VisibleUntilTick => IsOpen ? UntilTick : 0;

    /// <summary>打开（或覆盖）窗口。<paramref name="windowTicks"/> 必须 ≥ 1。</summary>
    public void Open(long tick, int windowTicks, bool penaltyOnMiss)
    {
        if (windowTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowTicks), windowTicks, "保护窗口长度必须为正。");
        }

        if (tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick, "决策 tick 不得为负。");
        }

        IsOpen = true;
        StartTick = tick;
        UntilTick = tick + windowTicks - 1;
        PenaltyOnMiss = penaltyOnMiss;
        Absorbed = false;
    }

    /// <summary>结算 tick 是否被当前窗口覆盖（闭区间 <c>[StartTick, UntilTick]</c>）。</summary>
    public bool Covers(long tick)
    {
        return IsOpen && tick >= StartTick && tick <= UntilTick;
    }

    /// <summary>标记命中（免伤）。</summary>
    public void Absorb()
    {
        Absorbed = true;
    }

    /// <summary>
    /// 窗口是否已过 <paramref name="tick"/> 的结束边界（调用方应在该 tick 的结算之后再查）。
    /// </summary>
    public bool ExpiredAt(long tick)
    {
        return IsOpen && tick > UntilTick;
    }

    /// <summary>关闭窗口；返回“是否判为猜错”（仅当标记了 <see cref="PenaltyOnMiss"/> 且从未命中）。</summary>
    public bool CloseAndReportMiss()
    {
        bool missed = IsOpen && PenaltyOnMiss && !Absorbed;
        Clear();
        return missed;
    }

    /// <summary>强制清空（剧本重置用）。</summary>
    public void Clear()
    {
        IsOpen = false;
        StartTick = 0;
        UntilTick = 0;
        PenaltyOnMiss = false;
        Absorbed = false;
    }
}
