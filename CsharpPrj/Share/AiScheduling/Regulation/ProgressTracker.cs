using System;
using System.Collections.Generic;

namespace Share.AiScheduling.Regulation;

/// <summary>
/// 跨轮进度追踪器（有状态，通用）：累计无进展步数 + 产物长度滑动窗口（EWMA 平滑速率 rate_t 的输入）。
/// 无进展判定：产物长度未增长（≤ 上轮）或评估未通过；有进展则清零无进展步数。
/// 状态与逻辑内聚于此，供任何"跨轮自调节"场景（如边界压力治理）复用。
/// </summary>
public sealed class ProgressTracker
{
    /// <summary>上轮产物长度。</summary>
    public int LastResultLength { get; private set; }

    /// <summary>累计无进展步数（产物长度未增长或评估未通过时累加；有进展清零）。</summary>
    public int NoProgressSteps { get; private set; }

    private readonly Queue<int> _lengths = new();

    /// <summary>
    /// 登记一轮产物长度：更新无进展步数 + 维护长度滑动窗口（上限 windowSize）。
    /// </summary>
    /// <param name="resultTextLength">本轮产物长度。</param>
    /// <param name="evalIsPass">本轮评估是否通过（无进展判定的一部分）。</param>
    /// <param name="windowSize">长度滑动窗口上限（EWMA 输入）。</param>
    public void Track(int resultTextLength, bool evalIsPass, int windowSize)
    {
        bool progress = resultTextLength > LastResultLength && evalIsPass;
        NoProgressSteps = progress ? 0 : NoProgressSteps + 1;
        LastResultLength = resultTextLength;

        _lengths.Enqueue(resultTextLength);
        int cap = Math.Max(1, windowSize);
        while (_lengths.Count > cap) _lengths.Dequeue();
    }

    /// <summary>当前长度窗口（旧→新）。</summary>
    public IReadOnlyList<int> Lengths => _lengths.ToArray();

    /// <summary>由长度窗口计算 EWMA 平滑速率（不足两段返回 null，回退单步速率）。</summary>
    public double? SmoothedRate(double alpha) => EwmaSmoother.Ewma(_lengths.ToArray(), alpha);
}
