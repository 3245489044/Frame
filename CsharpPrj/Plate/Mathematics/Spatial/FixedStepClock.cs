using System;

namespace MathematicalAlgorithms.Spatial;

/// <summary>
/// 固定步长时钟：把可变帧时长折算成「本帧要走几步固定步」，并钳制单帧时长与追赶步数。
/// <ul>
///   <li>单帧 <c>deltaTime</c> 先钳到 <see cref="MaxFrameDelta"/>：帧变慢不会让下一步位移变大。</li>
///   <li>累加器 + 步数上限：慢帧最多补 <see cref="MaxSubSteps"/> 步，超出部分直接丢弃（不积压、不死亡螺旋）。</li>
/// </ul>
/// <para>确定性数学，不属于某个仿真。帧级时长钳制由 <see cref="ClampFrameDelta"/> 统一提供，
/// 引擎的两个入口（Standalone / Host）都必须先过这一步；固定步折算由角色物理使用。</para>
/// </summary>
public sealed class FixedStepClock
{
    /// <summary>帧级时长上限（秒）：首帧/卡顿尖峰不进入相机积分、统计与状态写入。</summary>
    public const float MaxFrameDeltaSeconds = 0.1f;

    /// <summary>
    /// 步边界容差（单位：步）。只用于判断「累加时间是否已到达整数步」，
    /// 吸收 float32 帧时长在长时间累加后的系统性偏差（例如 144 FPS 的 <c>1/144f</c>
    /// 每帧比真值偏大 ~5e-11 s，432 帧后在 3.0 s 边界差 ~2e-8 s ≈ 1.3e-6 步）。
    /// <para>取值依据：float32 在 16 ms 量级的分辨率约 1.9e-9 s，数千帧累加偏差 &lt; 1e-6 s（&lt; 6e-5 步）；
    /// 而任何合法帧率留下的帧余量都不小于 1/360 s ≈ 0.167 步，比该容差大约 1600 倍。
    /// 因此它只会把「因输入舍入差一丝」的整倍数时间点补回整步，不会把真正未到边界的帧误判成整步。</para>
    /// </summary>
    private const double StepBoundaryTolerance = 1e-4;

    private readonly float _fixedDelta;
    private readonly double _fixedDeltaExact;
    private readonly int _maxSubSteps;
    private readonly float _maxFrameDelta;
    private double _accumulator;

    public FixedStepClock(float fixedDelta, int maxSubSteps, float maxFrameDelta)
    {
        if (fixedDelta <= 0f)
            throw new ArgumentOutOfRangeException(nameof(fixedDelta), "固定步长必须为正");

        _fixedDelta = fixedDelta;
        _fixedDeltaExact = fixedDelta;
        _maxSubSteps = Math.Max(1, maxSubSteps);
        _maxFrameDelta = maxFrameDelta > 0f ? maxFrameDelta : float.PositiveInfinity;
    }

    public float FixedDelta => _fixedDelta;
    public int MaxSubSteps => _maxSubSteps;
    public float MaxFrameDelta => _maxFrameDelta;
    public float Accumulator => (float)_accumulator;

    /// <summary>推进一步：返回本帧应执行的固定步数（0 ⇒ 本帧不动）。</summary>
    public int Advance(float deltaTime)
    {
        if (deltaTime <= 0f) return 0;
        if (deltaTime > _maxFrameDelta) deltaTime = _maxFrameDelta;

        // 双精度累加 + 步边界容差取整：保留跨帧余量，同时让 1.0/2.0/3.0/10.0 s 等
        // 整倍数时间点不再因浮点舍入少走一步（旧实现用 float 累加且做逐次减法，会在边界丢步）。
        _accumulator += deltaTime;
        int steps = (int)Math.Floor(_accumulator / _fixedDeltaExact + StepBoundaryTolerance);
        if (steps > _maxSubSteps) steps = _maxSubSteps;

        _accumulator -= steps * _fixedDeltaExact;
        // 容差跨过边界：把数值上的负余量归零，避免下一帧被“欠账”拖住。
        if (_accumulator < 0d) _accumulator = 0d;

        // 到达步数上限仍有积压 ⇒ 丢弃，保证下一帧不会被历史欠账拖长（与旧语义一致）。
        if (steps >= _maxSubSteps && _accumulator >= _fixedDeltaExact)
            _accumulator = 0d;
        return steps;
    }

    public void Reset() => _accumulator = 0d;
}
