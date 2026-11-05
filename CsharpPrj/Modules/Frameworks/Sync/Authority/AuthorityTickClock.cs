namespace Modules.Sync.Authority;

/// <summary>
/// 权威端 tick 时钟：把 20Hz 模拟 tick 精确换算成 10Hz 决策 tick。
/// <para>
/// 换算不使用浮点、不按“每次推进加 0.5”这一类累加式做法，而是用整数有理累加器：
/// 每推进一个模拟 tick，累加 <c>DecisionHz / g</c>；累加到 <c>SimHz / g</c> 即产生一个决策 tick，
/// 并把累加器减去该阈值（留在 <c>[0, SimHz/g)</c> 内）。因此累加器有界、误差不随时间累积，
/// 跑任意长的时间后 tick 号都与 <see cref="DecisionTickAt"/> 的闭式结果一致。
/// </para>
/// <para>
/// 频率值来自契约（模拟 20Hz / 决策 10Hz），本类只接受注入，不在此声明协议常量。
/// 决策频率必须不大于模拟频率（一拍模拟最多产生一个决策边界）。
/// </para>
/// </summary>
public sealed class AuthorityTickClock
{
    private readonly int _simHz;
    private readonly int _decisionHz;
    private readonly int _simPerDecision;
    private readonly int _decisionPerSim;

    private long _simTick;
    private long _decisionTick;
    private int _accumulator;

    /// <summary>构造时钟。起点为 <c>SimTick=0 / DecisionTick=0</c>（初始状态，尚未推进）。</summary>
    public AuthorityTickClock(int simHz, int decisionHz)
    {
        if (simHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(simHz), simHz, "模拟频率必须为正。");
        }

        if (decisionHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(decisionHz), decisionHz, "决策频率必须为正。");
        }

        if (decisionHz > simHz)
        {
            throw new ArgumentOutOfRangeException(nameof(decisionHz), decisionHz, "决策频率不得高于模拟频率（一拍模拟最多一个决策边界）。");
        }

        int g = Gcd(simHz, decisionHz);
        _simHz = simHz;
        _decisionHz = decisionHz;
        _simPerDecision = simHz / g;
        _decisionPerSim = decisionHz / g;
    }

    /// <summary>模拟频率（由宿主注入契约值）。</summary>
    public int SimHz => _simHz;

    /// <summary>决策频率（由宿主注入契约值）。</summary>
    public int DecisionHz => _decisionHz;

    /// <summary>已推进的模拟 tick 数。</summary>
    public long SimTick => _simTick;

    /// <summary>当前决策 tick：等于已跨越的决策边界数，从 0 单调递增。</summary>
    public long DecisionTick => _decisionTick;

    /// <summary>有理累加器余量，恒在 <c>[0, SimHz/g)</c>；用于断言“无漂移”。</summary>
    public int Remainder => _accumulator;

    /// <summary>
    /// 推进一个模拟 tick。返回 true 表示本拍跨越了决策边界（调用方据此执行一次决策推进）。
    /// </summary>
    public bool AdvanceSimTick()
    {
        _simTick = checked(_simTick + 1);

        int next = _accumulator + _decisionPerSim;
        if (next >= _simPerDecision)
        {
            _accumulator = next - _simPerDecision;
            _decisionTick = checked(_decisionTick + 1);
            return true;
        }

        _accumulator = next;
        return false;
    }

    /// <summary>
    /// 批量推进 <paramref name="count"/> 个模拟 tick，返回跨越的决策边界数。
    /// 用于宿主按固定步长驱动（不依赖墙钟）。
    /// </summary>
    public int AdvanceSimTicks(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "模拟 tick 推进数不得为负。");
        }

        int decisions = 0;
        for (int i = 0; i < count; i++)
        {
            if (AdvanceSimTick())
            {
                decisions++;
            }
        }

        return decisions;
    }

    /// <summary>
    /// 闭式换算：给定模拟 tick 数，返回已跨越的决策边界数（即当时应有的决策 tick）。
    /// <c>floor(simTick * DecisionHz / SimHz)</c>，整数精确，与累加器路径互为校验。
    /// </summary>
    public long DecisionTickAt(long simTick)
    {
        if (simTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(simTick), simTick, "模拟 tick 不得为负。");
        }

        if (simTick > long.MaxValue / _decisionHz)
        {
            throw new ArgumentOutOfRangeException(nameof(simTick), simTick, "模拟 tick 超出可精确换算范围。");
        }

        return simTick * _decisionHz / _simHz;
    }

    /// <summary>
    /// 闭式反查：第 <paramref name="decisionTick"/> 个决策边界发生在第几个模拟 tick（从 1 起计）。
    /// <c>ceil(decisionTick * SimHz / DecisionHz)</c>。
    /// </summary>
    public long SimTickOfDecision(long decisionTick)
    {
        if (decisionTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(decisionTick), decisionTick, "决策 tick 不得为负。");
        }

        if (decisionTick > long.MaxValue / _simHz)
        {
            throw new ArgumentOutOfRangeException(nameof(decisionTick), decisionTick, "决策 tick 超出可精确换算范围。");
        }

        long numerator = checked(decisionTick * _simHz);
        return (numerator + _decisionHz - 1) / _decisionHz;
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            int t = a % b;
            a = b;
            b = t;
        }

        return a < 0 ? -a : a;
    }
}
