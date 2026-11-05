namespace Modules.Sync.Authority;

/// <summary>
/// 公共行动条（ATB）。
/// <para>推进公式：<c>Atb_{t+1} = clamp(Atb_t + chargePerTick, 0, AtbScale)</c>；技能释放或超频后清零。</para>
/// <para>钳制边界：<c>[0, AtbScale]</c>；<c>AtbScale</c> 来自契约（宿主注入），本类不声明该常量。</para>
/// </summary>
public sealed class AtbTrack
{
    private readonly int _max;

    /// <summary>构造行动条；上限必须为正。</summary>
    public AtbTrack(int max)
    {
        if (max <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(max), max, "ATB 上限必须为正。");
        }

        _max = max;
    }

    /// <summary>行动条上限（契约 <c>AtbScale</c>）。</summary>
    public int Max => _max;

    /// <summary>当前行动条值。</summary>
    public int Value { get; private set; }

    /// <summary>是否已满（满即满足规则 3 的 ATB 条件）。</summary>
    public bool IsFull => Value >= _max;

    /// <summary>充能 <paramref name="amount"/>，超上限钳制；返回实际充入量。</summary>
    public int Charge(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "充能量不得为负。");
        }

        int before = Value;
        Value = (int)Math.Min((long)_max, (long)Value + amount);
        return Value - before;
    }

    /// <summary>清零（技能释放 / 超频）。</summary>
    public void Clear()
    {
        Value = 0;
    }
}
