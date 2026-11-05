using System.Collections.Generic;

namespace Share.AiScheduling.Regulation;

/// <summary>
/// EWMA 平滑器（兼容门面）：实现单源在 <see cref="Modules.Regulation.EwmaSmoother"/>，
/// 本类型仅转发，禁止复制加权公式。
/// </summary>
public static class EwmaSmoother
{
    /// <summary>由升序（旧→新）的产物长度窗口计算 EWMA 平滑速率；不足两段返回 null（回退单步速率）。</summary>
    public static double? Ewma(IReadOnlyList<int> lengths, double alpha)
        => Modules.Regulation.EwmaSmoother.Ewma(lengths, alpha);

    /// <summary>加权算术平均（共享实现，供本模块各纯数值工具复用）。</summary>
    internal static double WeightedAverage(IReadOnlyList<double> values, IReadOnlyList<double> weights)
        => Modules.Regulation.EwmaSmoother.WeightedAverage(values, weights);
}
