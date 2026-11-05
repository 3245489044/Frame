namespace Modules.Sync.Authority;

/// <summary>
/// 四技能 PP 轨道。
/// <para>推进公式：<c>PP_i(t+1) = clamp(PP_i(t) + regen_i − consume_i, 0, PPmax_i)</c>。</para>
/// <para>
/// 细则：消耗为“扣减后钳到 0”，且调用方先用 <see cref="CanConsume"/> 判定，避免部分扣减；
/// 被动回复由 <see cref="RegenerateAll"/> 在固定周期（决策 tick 整除）调用一次，满槽钳制。
/// </para>
/// <para>槽位数来自契约 <c>SkillSlots</c>（宿主注入），本类不声明 4。</para>
/// </summary>
public sealed class PpTrack
{
    private readonly int[] _pp;
    private readonly int _maxPerSlot;

    /// <summary>构造 PP 轨道；槽位数为 1 及以上，单槽上限为正。</summary>
    public PpTrack(int slotCount, int maxPerSlot)
    {
        if (slotCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slotCount), slotCount, "技能槽位数必须为正。");
        }

        if (maxPerSlot <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPerSlot), maxPerSlot, "单槽 PP 上限必须为正。");
        }

        _pp = new int[slotCount];
        _maxPerSlot = maxPerSlot;
    }

    /// <summary>槽位数。</summary>
    public int SlotCount => _pp.Length;

    /// <summary>单槽 PP 上限。</summary>
    public int MaxPerSlot => _maxPerSlot;

    /// <summary>读取第 <paramref name="slot"/> 槽剩余 PP。</summary>
    public int this[int slot] => _pp[slot];

    /// <summary>所有槽位剩余 PP 之和（规则 3 的“是否还有技能可放”条件）。</summary>
    public int Total
    {
        get
        {
            int sum = 0;
            for (int i = 0; i < _pp.Length; i++)
            {
                sum += _pp[i];
            }

            return sum;
        }
    }

    /// <summary>把某槽置为指定值（剧本初始化用），越界钳制到 <c>[0, MaxPerSlot]</c>。</summary>
    public void Reset(int slot, int value)
    {
        _pp[ValidateSlot(slot)] = value < 0 ? 0 : (value > _maxPerSlot ? _maxPerSlot : value);
    }

    /// <summary>所有槽位先把指定值（剧本初始化用）。</summary>
    public void ResetAll(int value)
    {
        for (int i = 0; i < _pp.Length; i++)
        {
            Reset(i, value);
        }
    }

    /// <summary>是否扣得起 <paramref name="amount"/> 点（条件求值入口，保证原子性）。</summary>
    public bool CanConsume(int slot, int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "PP 消耗不得为负。");
        }

        return _pp[ValidateSlot(slot)] >= amount;
    }

    /// <summary>扣减 PP；不足时返回 false 且不改变状态。</summary>
    public bool TryConsume(int slot, int amount)
    {
        int index = ValidateSlot(slot);
        if (!CanConsume(index, amount))
        {
            return false;
        }

        _pp[index] -= amount;
        return true;
    }

    /// <summary>被动回复：所有槽位 +<paramref name="amount"/>，逐槽钳制到上限；返回实际总入账量。</summary>
    public int RegenerateAll(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "PP 回复量不得为负。");
        }

        int gained = 0;
        for (int i = 0; i < _pp.Length; i++)
        {
            int before = _pp[i];
            _pp[i] = (int)Math.Min((long)_maxPerSlot, (long)_pp[i] + amount);
            gained += _pp[i] - before;
        }

        return gained;
    }

    private int ValidateSlot(int slot)
    {
        if ((uint)slot >= (uint)_pp.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "技能槽位越界。");
        }

        return slot;
    }
}
