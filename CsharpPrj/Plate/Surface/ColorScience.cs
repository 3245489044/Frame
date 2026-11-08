#nullable enable

using System;

namespace UiBackend;

/// <summary>
/// 颜色科学（**载体无关、BCL-only**）：sRGB ↔ 线性、Lab/L*、装饰色相增量。
/// <para>宿主前端不得再各写一份：丝带色相、装饰色、背景 L* 三处消费同一实现。</para>
/// </summary>
public static class ColorScience
{
    /// <summary>装饰色彩度上限（C*）：丝带/细边只承载色相，不承载彩度。</summary>
    public const float DecorChroma = 3f;

    /// <summary>sRGB 8bit 通道 → 线性亮度分量（D65 相对亮度公式的输入）。</summary>
    public static double LinearChannel(int channel)
    {
        var value = Math.Clamp(channel, 0, 255) / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    /// <summary>线性分量 → sRGB 0..1。</summary>
    public static double Encode(double value)
    {
        value = value <= 0.0031308 ? 12.92 * value : 1.055 * Math.Pow(value, 1.0 / 2.4) - 0.055;
        return value;
    }

    /// <summary>sRGB → L*（D65）。</summary>
    public static double Lstar(int red, int green, int blue)
    {
        var luminance = 0.2126 * LinearChannel(red) + 0.7152 * LinearChannel(green) + 0.0722 * LinearChannel(blue);
        return luminance <= 0.008856 ? 903.3 * luminance : 116.0 * Math.Cbrt(luminance) - 16.0;
    }

    /// <summary>Lab → 8bit sRGB（D65）。与 <see cref="ShiftChroma"/> 的 Δ(a,b) 增量口径配套。</summary>
    public static void LabToRgb(double lstar, double a, double b, out int red, out int green, out int blue)
    {
        var fy = (lstar + 16.0) / 116.0;
        var fx = fy + a / 500.0;
        var fz = fy - b / 200.0;
        double Inverse(double t) => t * t * t > 0.008856 ? t * t * t : (t - 16.0 / 116.0) / 7.787;
        var x = 0.95047 * Inverse(fx);
        var y = Inverse(fy);
        var z = 1.08883 * Inverse(fz);
        var r = 3.2406 * x - 1.5372 * y - 0.4986 * z;
        var g = -0.9689 * x + 1.8758 * y + 0.0415 * z;
        var bl = 0.0557 * x - 0.2040 * y + 1.0570 * z;
        red = (int)Math.Clamp(Math.Round(Encode(r) * 255.0), 0, 255);
        green = (int)Math.Clamp(Math.Round(Encode(g) * 255.0), 0, 255);
        blue = (int)Math.Clamp(Math.Round(Encode(bl) * 255.0), 0, 255);
    }

    /// <summary>线性 RGB → Lab 的 (a,b) 分量（只算色相需要的两项，不算 L*）。</summary>
    public static void LabAB(double r, double g, double b, out double a, out double bOut)
    {
        var x = 0.4124 * r + 0.3576 * g + 0.1805 * b;
        var y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        var z = 0.0193 * r + 0.1192 * g + 0.9505 * b;
        double Forward(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116.0;
        var fx = Forward(x / 0.95047);
        var fy = Forward(y);
        var fz = Forward(z / 1.08883);
        a = 500.0 * (fx - fy);
        bOut = 200.0 * (fy - fz);
    }

    /// <summary>
    /// 把一个 8bit 像素的 (a,b) 沿 <paramref name="hueDegrees"/> 推进 <see cref="DecorChroma"/>，**L* 不变**。
    /// <para>原理：L* = 116·fy − 16，所以「L* 不动」等价于 Δfy = 0；此时
    /// Δa = 500·Δfx、Δb = −200·Δfz，用 fx/fz 的立方关系反解 ΔX/ΔZ，再过一次线性矩阵得到 ΔRGB。
    /// 取整后再在 ±1 邻域里挑亮度增量最小的格点（残差 ≤0.02 L*）。</para>
    /// </summary>
    public static (int R, int G, int B) ShiftChroma(int red, int green, int blue, float hueDegrees)
    {
        var radians = hueDegrees * Math.PI / 180.0;
        var linearR = LinearChannel(red);
        var linearG = LinearChannel(green);
        var linearB = LinearChannel(blue);
        var fx = Math.Cbrt(linearR / 0.95047);
        var fz = Math.Cbrt(linearB / 1.08883);
        var deltaX = 3.0 * 0.95047 * fx * fx * (DecorChroma * Math.Cos(radians) / 500.0);
        var deltaZ = 3.0 * 1.08883 * fz * fz * (-DecorChroma * Math.Sin(radians) / 200.0);
        // ΔRGB 是**线性光**增量，必须回编码成 8bit 才算台阶；直接拿线性当字节用会小 300 倍。
        var targetR = Math.Max(0.0, linearR + 3.2406 * deltaX - 0.4986 * deltaZ);
        var targetG = Math.Max(0.0, linearG - 0.9689 * deltaX + 0.0415 * deltaZ);
        var targetB = Math.Max(0.0, linearB + 0.0557 * deltaX + 1.0570 * deltaZ);
        var deltaR = (int)Math.Round(Encode(targetR) * 255.0) - red;
        var deltaG = (int)Math.Round(Encode(targetG) * 255.0) - green;
        var deltaB = (int)Math.Round(Encode(targetB) * 255.0) - blue;

        var best = (R: red + deltaR, G: green + deltaG, B: blue + deltaB);
        var bestLuminance = Math.Abs(0.2126 * deltaR + 0.7152 * deltaG + 0.0722 * deltaB);
        for (var dr = -1; dr <= 1; dr++)
        {
            for (var dg = -1; dg <= 1; dg++)
            {
                for (var db = -1; db <= 1; db++)
                {
                    var luminance = Math.Abs(0.2126 * (deltaR + dr) + 0.7152 * (deltaG + dg) + 0.0722 * (deltaB + db));
                    if (luminance >= bestLuminance) continue;
                    bestLuminance = luminance;
                    best = (R: red + deltaR + dr, G: green + deltaG + dg, B: blue + deltaB + db);
                }
            }
        }

        return (Math.Clamp(best.R, 0, 255), Math.Clamp(best.G, 0, 255), Math.Clamp(best.B, 0, 255));
    }
}
