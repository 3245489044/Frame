using System.Collections.Generic;

namespace Share.AiScheduling.Regulation;

/// <summary>
/// 跨轮进度追踪器（兼容门面）：状态与逻辑单源在 <see cref="Modules.Regulation.ProgressTracker"/>。
/// </summary>
public sealed class ProgressTracker
{
    private readonly Modules.Regulation.ProgressTracker _inner = new();

    /// <summary>上轮产物长度。</summary>
    public int LastResultLength => _inner.LastResultLength;

    /// <summary>累计无进展步数（产物长度未增长或评估未通过时累加；有进展清零）。</summary>
    public int NoProgressSteps => _inner.NoProgressSteps;

    /// <summary>登记一轮产物长度：更新无进展步数 + 维护长度滑动窗口（上限 windowSize）。</summary>
    public void Track(int resultTextLength, bool evalIsPass, int windowSize)
        => _inner.Track(resultTextLength, evalIsPass, windowSize);

    /// <summary>当前长度窗口（旧→新）。</summary>
    public IReadOnlyList<int> Lengths => _inner.Lengths;

    /// <summary>由长度窗口计算 EWMA 平滑速率（不足两段返回 null，回退单步速率）。</summary>
    public double? SmoothedRate(double alpha) => _inner.SmoothedRate(alpha);
}
