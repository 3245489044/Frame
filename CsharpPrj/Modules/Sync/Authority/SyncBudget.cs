namespace Modules.Sync.Authority;

/// <summary>
/// 单一资源 Sync（同步预算）。
/// <para>推进公式：<c>Sync_{t+1} = clamp(Sync_t + r − Σ(cᵢ·aᵢ) − c_mut, 0, S_max)</c>。</para>
/// <para>钳制边界：下界 0（消耗前必须先经 <see cref="CanSpend"/> 判定，因此不会出现负值）；上界 <see cref="Max"/>。</para>
/// </summary>
public sealed class SyncBudget
{
    private readonly int _max;

    /// <summary>构造预算。上限必须为正；初始值为 0，由剧本通过 <see cref="Reset"/> 显式设置。</summary>
    public SyncBudget(int max)
    {
        if (max <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(max), max, "Sync 上限必须为正。");
        }

        _max = max;
    }

    /// <summary>同步上限 <c>S_max</c>。</summary>
    public int Max => _max;

    /// <summary>当前同步预算。</summary>
    public int Value { get; private set; }

    /// <summary>把预算直接置为 <paramref name="value"/>（剧本初始化用），越界自动钳制。</summary>
    public void Reset(int value)
    {
        Value = Clamp(value);
    }

    /// <summary>基础恢复 <c>r</summary>；返回实际入账的增量（到达上限时为 0）。</summary>
    public int Regenerate(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "恢复量不得为负，扣减请用 TrySpend。");
        }

        int before = Value;
        Value = Clamp((int)Math.Min(int.MaxValue, (long)Value + amount));
        return Value - before;
    }

    /// <summary>返还（保护猜中回补）；上界钳制。</summary>
    public int Refund(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "返还量不得为负。");
        }

        int before = Value;
        Value = Clamp((int)Math.Min(int.MaxValue, (long)Value + amount));
        return Value - before;
    }

    /// <summary>是否付得起 <paramref name="cost"/>（条件求值必须先问这里，保证不会出现部分扣减）。</summary>
    public bool CanSpend(int cost)
    {
        if (cost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cost), cost, "消耗不得为负。");
        }

        return Value >= cost;
    }

    /// <summary>扣减 <paramref name="cost"/>；余额不足时返回 false 且不改变状态（原子语义）。</summary>
    public bool TrySpend(int cost)
    {
        if (!CanSpend(cost))
        {
            return false;
        }

        Value -= cost;
        return true;
    }

    private int Clamp(int value)
    {
        if (value < 0)
        {
            return 0;
        }

        return value > _max ? _max : value;
    }
}
