using System;
using System.Collections.Generic;

namespace Share.AiScheduling.Regulation;

/// <summary>
/// 指数加权移动平均（EWMA）平滑器（纯函数）：窗口内逐段速率以指数衰减权重加权平均，抗单轮抖动。
/// weightᵢ = α·(1-α)^i（i=0 对应最新速率，权重最高）。
/// </summary>
public static class EwmaSmoother
{
    /// <summary>由升序（旧→新）的产物长度窗口计算 EWMA 平滑速率；不足两段返回 null（回退单步速率）。</summary>
    public static double? Ewma(IReadOnlyList<int> lengths, double alpha)
    {
        if (lengths.Count < 2) return null;

        // 逐段速率，最新在前（rates[0] = 最近一轮/上一轮）
        var rates = new List<double>(lengths.Count - 1);
        for (int i = lengths.Count - 1; i >= 1; i--)
        {
            double prev = lengths[i - 1];
            rates.Add(prev > 0 ? (double)lengths[i] / prev : 1.0);
        }

        double a = Math.Clamp(alpha, 0d, 1d);
        var weights = new List<double>(rates.Count);
        for (int i = 0; i < rates.Count; i++)
            weights.Add(a * Math.Pow(1 - a, i));

        return WeightedAverage(rates, weights);
    }

    /// <summary>加权算术平均（共享实现，供本模块各纯数值工具复用）。</summary>
    internal static double WeightedAverage(IReadOnlyList<double> values, IReadOnlyList<double> weights)
    {
        double ws = 0d, vs = 0d;
        for (int i = 0; i < values.Count && i < weights.Count; i++)
        {
            ws += weights[i];
            vs += weights[i] * values[i];
        }
        return ws > 0 ? vs / ws : 0d;
    }
}
