namespace Modules.Compute.Simd;

/// <summary>
/// 确定性守卫：把「哪些核允许进入世界生成」写成可读常量，避免后续无意引入近似/宽度自适应。
/// <para>A 档（逐位一致）下：世界生成只允许<b>精确闭式</b>；SIMD 宽度不得参与世界输出决策，
/// 需要跨架构一致时固定 lane 宽度 + 标量尾复现同一顺序。</para>
/// </summary>
public static class SimdGuards
{
    /// <summary>当前施工档位（A = 逐位一致）。</summary>
    public const string DeterminismGrade = "A";

    /// <summary>近似函数（<see cref="ApproxMath"/>）是否允许进入世界生成通道。</summary>
    public const bool ApproxAllowedInWorldGeneration = false;

    /// <summary><c>Vector&lt;float&gt;</c> 宽度；仅用于预算/诊断，不得用于决定世界输出。</summary>
    public static int VectorWidth => SimdKernel.Width;

    /// <summary>世界生成路径上的断言：调用近似函数/宽度自适应前必须先过这一关。</summary>
    public static void EnsureWorldGeneration(string kernel, bool usesApproximation, bool widthDependent)
    {
        if (usesApproximation && !ApproxAllowedInWorldGeneration)
            throw new InvalidOperationException(
                $"[Compute/A] 世界生成禁止近似闭式：{kernel}。仅允许精确闭式（PowI/SmoothStep 等乘法闭式）。");
        if (widthDependent)
            throw new InvalidOperationException(
                $"[Compute/A] 世界生成禁止以 SIMD 宽度为分支：{kernel}。请固定 lane 宽度 + 标量尾复现同一顺序。");
    }
}
