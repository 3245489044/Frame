namespace Modules.Sync.Authority;

/// <summary>
/// 脚本化敌人的前摇/结算节拍（固定周期，不引入行为树、不引入随机数）。
/// <para>
/// 设周期 <c>I</c>、前摇 <c>W</c>：对任意决策 tick <c>t</c>，
/// <c>phase = t mod I</c>；<c>phase &lt; W</c> 时前摇可见（<see cref="TelegraphCue.Projectile"/>），
/// <c>phase == W</c> 时结算伤害（前摇结束的下一拍立刻命中）。
/// </para>
/// <para>因此“看到前摇”与“挨打”之间恰好 <c>W</c> 个决策 tick；保护窗口长度 ≥ <c>W + 1</c> 才能在窗口内覆盖结算。</para>
/// </summary>
public sealed class EnemyTelegraphScript
{
    private readonly int _intervalTicks;
    private readonly int _windupTicks;

    /// <summary>构造脚本；要求 <c>intervalTicks &gt; windupTicks ≥ 1</c>，保证一个周期内至少有一拍“无前摇”。</summary>
    public EnemyTelegraphScript(int intervalTicks, int windupTicks)
    {
        if (windupTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windupTicks), windupTicks, "前摇长度必须为正。");
        }

        if (intervalTicks <= windupTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalTicks), intervalTicks, "攻击周期必须大于前摇长度。");
        }

        _intervalTicks = intervalTicks;
        _windupTicks = windupTicks;
    }

    /// <summary>攻击周期（决策 tick）。</summary>
    public int IntervalTicks => _intervalTicks;

    /// <summary>前摇长度（决策 tick）。</summary>
    public int WindupTicks => _windupTicks;

    /// <summary>该 tick 的可见前摇线索。</summary>
    public TelegraphCue CueAt(long tick)
    {
        if (tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick, "决策 tick 不得为负。");
        }

        return tick % _intervalTicks < _windupTicks ? TelegraphCue.Projectile : TelegraphCue.None;
    }

    /// <summary>该 tick 是否结算一次伤害。</summary>
    public bool ResolvesAt(long tick)
    {
        if (tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick, "决策 tick 不得为负。");
        }

        return tick % _intervalTicks == _windupTicks;
    }
}
