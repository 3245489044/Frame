#nullable enable

using System;

namespace MathematicalAlgorithms.Lighting;

/// <summary>虚拟矩阵光样本（页面/chrome 装饰数学的唯一输入，来自引擎光场）。</summary>
public readonly record struct AmbientLightSample(
    float Angle, float SpotX, float SpotY, float Shade,
    float ShadowX, float ShadowY, float Phase, int TintArgb)
{
    public static readonly AmbientLightSample Declared = new(135f, 0.30f, 0.18f, 1f, 0f, 0f, 0.5f, unchecked((int)0xFFDCEBFF));
}

/// <summary>CIELAB（L*, a*, b*）。</summary>
public readonly record struct LabColor(double L, double A, double B);

/// <summary>页面场/chrome 的唯一数学（虚拟矩阵光 → 色相/径向/底色）。</summary>
public static class SurfaceAmbientMath
{
    public const float FamilyHueBase = 190f;
    public const float FamilySpan = 40f;
    public const float DecorChroma = 3f;
    public const float DefaultHueBudget = 150f;
    public const float EdgeAlpha = 255f;

    public static float HueBudget(float declared) => declared > 0f ? declared : DefaultHueBudget;

    public static float ShapePhase(in AmbientLightSample light) => light.Phase;

    /// <summary>径向光在该点的合成白量（0..1；焦点 0.12 + 线性衰减）。</summary>
    public static float RadialCoverage(
        float radialAlpha, float radialRadius,
        float x, float y, int width, int height, float scale,
        in AmbientLightSample light)
    {
        if (radialAlpha <= 0f || radialRadius <= 0f) return 0f;
        var shade = Math.Clamp(light.Shade, 0f, 1f);
        if (shade <= 0.02f) return 0f;
        var radius = Math.Max(8f, radialRadius * scale);
        var dx = x - width * light.SpotX;
        var dy = y - height * light.SpotY;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance >= radius) return 0f;
        var focus = radius * 0.12f;
        var falloff = distance <= focus ? 1.0 : 1.0 - (distance - focus) / (radius - focus);
        return (float)(radialAlpha * shade * falloff);
    }

    /// <summary>量化光源签名（方向 10° / 阴影 0.1 / 相位 0.1 / 光斑位置 0.05）。</summary>
    public static string LightToken(in AmbientLightSample light)
    {
        var angle = (int)Math.Round(light.Angle / 10f) * 10;
        var spotX = (int)Math.Round(light.SpotX / 0.05f);
        var spotY = (int)Math.Round(light.SpotY / 0.05f);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"A{angle}:X{light.ShadowX:F1}:Y{light.ShadowY:F1}:P{light.Phase:F1}:S{spotX}:T{spotY}");
    }

    /// <summary>sRGB 通道 → 线性亮度分量。</summary>
    public static double LinearChannel(int channel)
    {
        var value = Math.Clamp(channel, 0, 255) / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    /// <summary>sRGB → L*（D65）。</summary>
    public static double Lstar(int red, int green, int blue)
    {
        var luminance = 0.2126 * LinearChannel(red) + 0.7152 * LinearChannel(green) + 0.0722 * LinearChannel(blue);
        return luminance <= 0.008856 ? 903.3 * luminance : 116.0 * Math.Cbrt(luminance) - 16.0;
    }

    private static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116.0;

    private static double Inverse(double t) => t * t * t > 0.008856 ? t * t * t : (t - 16.0 / 116.0) / 7.787;

    private static double Encode(double value)
        => value <= 0.0031308 ? 12.92 * value : 1.055 * Math.Pow(value, 1.0 / 2.4) - 0.055;

    /// <summary>线性 RGB → Lab 的 (a,b)。</summary>
    public static void LabAB(double r, double g, double b, out double a, out double bOut)
    {
        var x = 0.4124 * r + 0.3576 * g + 0.1805 * b;
        var y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        var z = 0.0193 * r + 0.1192 * g + 0.9505 * b;
        var fx = F(x / 0.95047);
        var fy = F(y);
        var fz = F(z / 1.08883);
        a = 500.0 * (fx - fy);
        bOut = 200.0 * (fy - fz);
    }

    /// <summary>Lab → 打包 ARGB。</summary>
    public static int LabToArgb(double lstar, double a, double b, int alpha)
    {
        var fy = (lstar + 16.0) / 116.0;
        var x = 0.95047 * Inverse(fy + a / 500.0);
        var y = Inverse(fy);
        var z = 1.08883 * Inverse(fy - b / 200.0);
        var r = 3.2406 * x - 1.5372 * y - 0.4986 * z;
        var g = -0.9689 * x + 1.8758 * y + 0.0415 * z;
        var bl = 0.0557 * x - 0.2040 * y + 1.0570 * z;
        return PackArgb(alpha, Encode(r), Encode(g), Encode(bl));
    }

    /// <summary>装饰色：保留底色 L*，只承载固定彩度与给定色相。</summary>
    public static int DecorColor(double lstar, double backdropA, double backdropB, float hueDegrees, int alpha)
    {
        var radians = hueDegrees * Math.PI / 180.0;
        var targetA = backdropA + DecorChroma * Math.Cos(radians);
        var targetB = backdropB + DecorChroma * Math.Sin(radians);
        return LabToArgb(lstar, targetA, targetB, alpha);
    }

    /// <summary>页面 45° 渐变 + 径向光后的底色 Lab（单一实现）。</summary>
    public static LabColor BackdropLab(
        int fromArgb, int toArgb,
        float x, float y, int width, int height, float scale,
        float radialAlpha, float radialRadius,
        in AmbientLightSample light)
    {
        var (fromR, fromG, fromB) = RgbOf(fromArgb);
        var (toR, toG, toB) = RgbOf(toArgb);
        var t = Math.Clamp((x + y) / Math.Max(1f, width + height), 0f, 1f);
        var red = fromR + (toR - fromR) * t;
        var green = fromG + (toG - fromG) * t;
        var blue = fromB + (toB - fromB) * t;
        var radial = RadialCoverage(radialAlpha, radialRadius, x, y, width, height, scale, light);
        if (radial > 0f)
        {
            red += (255.0 - red) * radial;
            green += (255.0 - green) * radial;
            blue += (255.0 - blue) * radial;
        }

        var r = (int)Math.Round(red);
        var g = (int)Math.Round(green);
        var b = (int)Math.Round(blue);
        LabAB(LinearChannel(r), LinearChannel(g), LinearChannel(b), out var a, out var bOut);
        return new LabColor(Lstar(r, g, b), a, bOut);
    }

    public static (double R, double G, double B) RgbOf(int argb)
        => (((argb >> 16) & 0xFF), ((argb >> 8) & 0xFF), (argb & 0xFF));

    private static int PackArgb(int alpha, double r, double g, double b)
        => unchecked((Math.Clamp(alpha, 0, 255) << 24)
            | (ToByte(r) << 16) | (ToByte(g) << 8) | ToByte(b));

    private static int ToByte(double value)
        => (int)Math.Clamp(Math.Round(value * 255.0), 0, 255);

}
