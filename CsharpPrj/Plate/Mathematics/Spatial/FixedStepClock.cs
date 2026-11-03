using System;

namespace MathematicalAlgorithms.Spatial;

/// <summary>
/// 固定步长时钟：把可变帧时长折算成「本帧要走几步固定步」，并钳制单帧时长与追赶步数。
/// <ul>
///   <li>单帧 <c>deltaTime</c> 先钳到 <see cref="MaxFrameDelta"/>：帧变慢不会让下一步位移变大。</li>
///   <li>累加器 + 步数上限：慢帧最多补 <see cref="MaxSubSteps"/> 步，超出部分直接丢弃（不积压、不死亡螺旋）。</li>
/// </ul>
/// <para>确定性数学，不属于某个仿真：引擎与矩阵共用同一份折算规则。</para>
/// </summary>
public sealed class FixedStepClock
{
    private readonly float _fixedDelta;
    private readonly int _maxSubSteps;
    private readonly float _maxFrameDelta;
    private float _accumulator;

    public FixedStepClock(float fixedDelta, int maxSubSteps, float maxFrameDelta)
    {
        if (fixedDelta <= 0f)
            throw new ArgumentOutOfRangeException(nameof(fixedDelta), "固定步长必须为正");

        _fixedDelta = fixedDelta;
        _maxSubSteps = Math.Max(1, maxSubSteps);
        _maxFrameDelta = maxFrameDelta > 0f ? maxFrameDelta : float.PositiveInfinity;
    }

    public float FixedDelta => _fixedDelta;
    public int MaxSubSteps => _maxSubSteps;
    public float MaxFrameDelta => _maxFrameDelta;
    public float Accumulator => _accumulator;

    /// <summary>推进一步：返回本帧应执行的固定步数（0 ⇒ 本帧不动）。</summary>
    public int Advance(float deltaTime)
    {
        if (deltaTime <= 0f) return 0;
        if (deltaTime > _maxFrameDelta) deltaTime = _maxFrameDelta;

        _accumulator += deltaTime;
        int steps = 0;
        while (_accumulator >= _fixedDelta && steps < _maxSubSteps)
        {
            _accumulator -= _fixedDelta;
            steps++;
        }

        // 到达步数上限仍有积压 ⇒ 丢弃，保证下一帧不会被历史欠账拖长。
        if (steps >= _maxSubSteps && _accumulator >= _fixedDelta)
            _accumulator = 0f;
        return steps;
    }

    public void Reset() => _accumulator = 0f;
}
