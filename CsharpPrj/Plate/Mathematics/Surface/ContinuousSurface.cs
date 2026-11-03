namespace MathematicalAlgorithms.Surface;

/// <summary>
/// 连续标量场的纯算法基元：三线性插值、梯度与亚格求根。
/// <para>输入只是 8 个格心标量与局部坐标，不认方块、不认区块、不认引擎类型；
/// GPU 侧的同名函数（RayMarchAlgorithm）逐式镜像这里的运算顺序与迭代次数。</para>
/// </summary>
public static class ContinuousSurface
{
    /// <summary>亚格求根固定迭代次数（与 GPU 侧 3 次三线性二分一致）。</summary>
    public const int RootIterations = 3;

    /// <summary>格心标量三线性插值。fx/fy/fz 为相对格心的局部坐标，取值 [0,1]。</summary>
    public static float Trilinear(
        float c000, float c100, float c010, float c110,
        float c001, float c101, float c011, float c111,
        float fx, float fy, float fz)
    {
        float a = c000 + (c100 - c000) * fx;
        float b = c010 + (c110 - c010) * fx;
        float c = c001 + (c101 - c001) * fx;
        float d = c011 + (c111 - c011) * fx;
        float e = a + (b - a) * fy;
        float f = c + (d - c) * fy;
        return e + (f - e) * fz;
    }

    /// <summary>
    /// 三线性场的平均梯度（8 角点的轴向中心差分）。与 GPU 的角点差分同式，
    /// 用于面法线，避免在格边界出现逐格跳变的法线。
    /// </summary>
    public static void Gradient(
        float c000, float c100, float c010, float c110,
        float c001, float c101, float c011, float c111,
        out float gx, out float gy, out float gz)
    {
        float x0 = (c000 + c010 + c001 + c011) * 0.25f;
        float x1 = (c100 + c110 + c101 + c111) * 0.25f;
        float y0 = (c000 + c100 + c001 + c101) * 0.25f;
        float y1 = (c010 + c110 + c011 + c111) * 0.25f;
        float z0 = (c000 + c100 + c010 + c110) * 0.25f;
        float z1 = (c001 + c101 + c011 + c111) * 0.25f;
        gx = x1 - x0;
        gy = y1 - y0;
        gz = z1 - z0;
    }

    /// <summary>
    /// 在 [lo,hi] 内二分定位标量场的阈值边界；<paramref name="sample"/> 返回该点的连续标量。
    /// 迭代次数固定，保证 CPU/GPU 与跨运行确定性。
    /// </summary>
    public static float Bisect(float lo, float hi, float threshold, Func<float, float> sample, int iterations = RootIterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            float mid = (lo + hi) * 0.5f;
            if (sample(mid) > threshold) hi = mid;
            else lo = mid;
        }
        return hi;
    }
}
