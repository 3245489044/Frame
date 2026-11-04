namespace MathematicalAlgorithms.Fitting;

/// <summary>
/// 拟合结果状态：所有失败都是显式状态，禁止用 0 系数或空曲线冒充成功。
/// </summary>
public enum FitStatus
{
    /// <summary>拟合成功，曲线与残差可用。</summary>
    Ok,

    /// <summary>样本数小于 <see cref="FitOptions.MinimumSamples"/>。</summary>
    InsufficientSamples,

    /// <summary>输入非法（空集、非有限值、缺失基函数等）。</summary>
    InvalidInput,

    /// <summary>设计矩阵秩不足或解非有限。</summary>
    Degenerate,

    /// <summary>迭代/网格搜索未能得到有限残差。</summary>
    NonConverged,
}
