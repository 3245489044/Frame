namespace Modules.Compute.Simd;

/// <summary>
/// 近似闭式函数（<b>仅限非世界确定性通道</b>：光照衰减、分布权重、动画/风向、AO 柔化）。
/// <para>A 档硬纪律：任何近似函数进入 <c>HeightFormula</c>/<c>CaveField</c>/<c>OreField</c> 前，
/// 必须与 HLSL 同一表达式并给出误差测试；否则破坏 CPU/GPU 一致性与存档确定性。</para>
/// <para>误差目标（相对/绝对）：Exp ≤2e-4、SinCos ≤1e-6、Tanh ≤1e-5；<see cref="PowI"/> 与
/// <see cref="SmoothStep"/> 为<b>精确</b>闭式（不引入额外误差）。</para>
/// </summary>
public static class ApproxMath
{
    private const float Ln2 = 0.6931471805599453f;
    private const double PiOver2 = 1.5707963267948966;
    private const double TwoPi = 6.283185307179586;

    /// <summary>e^x（区间缩减 + 5 阶多项式）。定义域裁剪：x &gt; 88 ⇒ +∞，x &lt; -87 ⇒ 0。</summary>
    public static float Exp(float x)
    {
        if (float.IsNaN(x))
            return x;
        if (x > 88f)
            return float.PositiveInfinity;
        if (x < -87f)
            return 0f;

        float k = MathF.Round(x / Ln2);
        float r = x - k * Ln2;

        // 1 + r + r²/2 + r³/6 + r⁴/24 + r⁵/120（Horner）
        float p = 1f + r * (1f + r * (0.5f + r * (0.16666667f + r * (0.041666668f + r * 0.008333334f))));
        return ScaleByPowerOfTwo(p, (int)k);
    }

    /// <summary>sin/cos 同步求值（区间缩减到 [-π/4, π/4] + 7 阶多项式）。</summary>
    public static void SinCos(float x, out float sin, out float cos)
    {
        double xd = (double)x;
        xd %= TwoPi;

        int quadrant = (int)Math.Round(xd / PiOver2);
        double r = xd - quadrant * PiOver2;
        double r2 = r * r;

        double s = r * (1.0 + r2 * (-1.0 / 6.0 + r2 * (1.0 / 120.0 + r2 * (-1.0 / 5040.0))));
        double c = 1.0 + r2 * (-0.5 + r2 * (1.0 / 24.0 + r2 * (-1.0 / 720.0)));

        switch (quadrant & 3)
        {
            case 0:
                sin = (float)s;
                cos = (float)c;
                break;
            case 1:
                sin = (float)c;
                cos = (float)(-s);
                break;
            case 2:
                sin = (float)(-s);
                cos = (float)(-c);
                break;
            default:
                sin = (float)(-c);
                cos = (float)s;
                break;
        }
    }

    /// <summary>tanh(x) = 1 − 2/(e^{2x}+1)；|x| &gt; 8 直接取 ±1。</summary>
    public static float Tanh(float x)
    {
        if (x > 8f)
            return 1f;
        if (x < -8f)
            return -1f;

        float e = Exp(2f * x);
        return 1f - 2f / (e + 1f);
    }

    /// <summary>整数幂（平方乘）：<b>精确</b>（只由乘法构成）。</summary>
    public static float PowI(float value, int exponent)
    {
        if (exponent == 0)
            return 1f;

        int e = exponent < 0 ? -exponent : exponent;
        float result = 1f;
        float factor = value;

        while (e > 0)
        {
            if ((e & 1) != 0)
                result *= factor;
            factor *= factor;
            e >>= 1;
        }

        return exponent < 0 ? 1f / result : result;
    }

    /// <summary>平滑步进 <c>3t² − 2t³</c>（与现有噪声权重同式，<b>精确</b>）。</summary>
    public static float SmoothStep(float t) => t * t * (3f - 2f * t);

    private static float ScaleByPowerOfTwo(float value, int exponent)
    {
        if (exponent > 127)
            return float.PositiveInfinity;
        if (exponent < -126)
            return 0f;
        return BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value) + (exponent << 23));
    }
}
