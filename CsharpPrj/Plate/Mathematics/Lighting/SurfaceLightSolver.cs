#nullable enable

using System;

namespace MathematicalAlgorithms.Lighting;

/// <summary>光板映射参数（与 <c>SurfaceLightFieldOptions</c> 字段一一对应）。</summary>
public readonly record struct SurfaceLightParameters(
    float AnchorXcm, float AnchorYcm, float AnchorZcm,
    float AxisXx, float AxisXy, float AxisXz,
    float AxisYx, float AxisYy, float AxisYz,
    float SunAzimuthDegrees, float SunElevationDegrees,
    float SunIntensity, float Ambient, float Warmth, int TintArgb,
    float NormalBias, float LocalDeviation, float SpecularExponent, float TintTransfer)
{
    /// <summary>引擎/探测侧默认参数（唯一默认值来源）。</summary>
    public static readonly SurfaceLightParameters Default = new(
        0f, 0f, 0f,
        0.6f, 0f, 0f,
        0f, 0f, 0.6f,
        135f, 38f, 0.85f, 0.18f, 0.12f, unchecked((int)0xFFDCEBFF),
        0.9f, 0.35f, 12f, 0.30f);
}

/// <summary>单个光场单元（方向/镜面/透射/底色）。</summary>
public readonly record struct SurfaceLightCellSample(
    float DirectionX, float DirectionY, float DirectionZ,
    float Specular, float Transmission, int TintArgb);

/// <summary>
/// 光板唯一数学（引擎与 Frame 共用）：太阳方向、法线、半程/镜面、色温调制、签名。
/// <para>禁止任何调用点再写同一公式；GPU 侧只镜像 <see cref="Shade"/> 的逐像素版本。</para>
/// </summary>
public static class SurfaceLightSolver
{
    private const float DegToRad = MathF.PI / 180f;

    /// <summary>太阳方位/仰角 → 指向光单位向量。</summary>
    public static (float X, float Y, float Z) SunDirection(float azimuthDegrees, float elevationDegrees)
    {
        var azimuth = azimuthDegrees * DegToRad;
        var elevation = elevationDegrees * DegToRad;
        var planar = MathF.Cos(elevation);
        return Normalize(planar * MathF.Cos(azimuth), -planar * MathF.Sin(azimuth), MathF.Sin(elevation));
    }

    /// <summary>安全归一化（零向量回落 +Z）。</summary>
    public static (float X, float Y, float Z) Normalize(float x, float y, float z)
    {
        var length = MathF.Sqrt(x * x + y * y + z * z);
        if (length < 1e-6f) return (0f, 0f, 1f);
        return (x / length, y / length, z / length);
    }

    /// <summary>点积。</summary>
    public static float Dot((float X, float Y, float Z) a, (float X, float Y, float Z) b)
        => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>UI DIP → 光板矩阵厘米坐标。</summary>
    public static (float X, float Y, float Z) PointAt(in SurfaceLightParameters p, float x, float y)
        => (
            p.AnchorXcm + p.AxisXx * x + p.AxisYx * y,
            p.AnchorYcm + p.AxisXy * x + p.AxisYy * y,
            p.AnchorZcm + p.AxisXz * x + p.AxisYz * y);

    /// <summary>密度梯度 → 法线（ζ 常量拉向观察者，近零梯度不退化）。</summary>
    public static (float X, float Y, float Z) Normal(float densityX, float densityY, float normalBias)
        => Normalize(-densityX, -densityY, MathF.Max(1e-3f, normalBias));


    /// <summary>宏状态 → 单元采样（唯一实现）。</summary>
    public static SurfaceLightCellSample Cell(
        in SurfaceLightParameters p,
        float density, float macroR, float macroG, float macroB,
        float gradientX, float gradientY)
    {
        var sun = SunDirection(p.SunAzimuthDegrees, p.SunElevationDegrees);
        var half = Normalize(sun.X, sun.Y, sun.Z + 1f);
        var normal = Normal(gradientX, gradientY, p.NormalBias);
        var specular = MathF.Pow(MathF.Max(0f, Dot(normal, half)), MathF.Max(1f, p.SpecularExponent));
        var direction = Normalize(
            sun.X + p.LocalDeviation * normal.X,
            sun.Y + p.LocalDeviation * normal.Y,
            sun.Z + p.LocalDeviation * (normal.Z - 1f));

        var clamped = Math.Clamp(density, 0f, 1f);
        var transmission = Math.Clamp(0.55f + 0.45f * (1f - clamped), 0f, 1f);
        var tint = TintOf(p.TintArgb, p.TintTransfer, macroR, macroG, macroB);
        return new SurfaceLightCellSample(
            direction.X, direction.Y, direction.Z,
            Math.Clamp(specular, 0f, 1f), transmission,
            PackArgb(255, tint.R, tint.G, tint.B));
    }

    /// <summary>逐像素着色（HLSL 镜像的同式 CPU 版本）。</summary>
    public static (float R, float G, float B) Shade(
        in SurfaceLightParameters p,
        float baseR, float baseG, float baseB,
        float normalX, float normalY, float normalZ)
    {
        var sun = SunDirection(p.SunAzimuthDegrees, p.SunElevationDegrees);
        var half = Normalize(sun.X, sun.Y, sun.Z + 1f);
        var n = Normalize(normalX, normalY, MathF.Max(1e-3f, normalZ));
        var diffuse = MathF.Max(0f, Dot(n, sun));
        var specular = MathF.Pow(MathF.Max(0f, Dot(n, half)), MathF.Max(1f, p.SpecularExponent));
        var light = Math.Clamp(p.Ambient + p.SunIntensity * diffuse, 0f, 2f);
        return (
            Math.Clamp(baseR * light + specular * 0.35f, 0f, 1f),
            Math.Clamp(baseG * light + specular * 0.35f, 0f, 1f),
            Math.Clamp(baseB * light + specular * 0.35f, 0f, 1f));
    }

    /// <summary>宏材质色 → 全局底色调制。</summary>
    public static (float R, float G, float B) TintOf(
        int globalArgb, float transfer, float macroR, float macroG, float macroB)
    {
        var (globalR, globalG, globalB) = RgbOf(globalArgb);
        var lum = Math.Clamp((macroR + macroG + macroB) / 3f, 0f, 1f);
        var k = Math.Clamp(transfer, 0f, 1f);
        return (
            Math.Clamp(globalR + (macroR - lum) * k, 0f, 1f),
            Math.Clamp(globalG + (macroG - lum) * k, 0f, 1f),
            Math.Clamp(globalB + (macroB - lum) * k, 0f, 1f));
    }

    /// <summary>ARGB → 0..1 RGB。</summary>
    public static (float R, float G, float B) RgbOf(int argb)
        => (((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f);

    /// <summary>打包 ARGB。</summary>
    public static int PackArgb(int alpha, float r, float g, float b)
        => unchecked((alpha << 24) | (ToByte(r) << 16) | (ToByte(g) << 8) | ToByte(b));

    /// <summary>0..1 → 0..255（四舍五入夹取）。</summary>
    public static int ToByte(float value)
        => (int)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);

    /// <summary>签名混合（稳定、可复算）。</summary>
    public static long Mix(long left, long right)
    {
        unchecked
        {
            return left ^ (right + unchecked((long)0x9E3779B97F4A7C15UL) + (left << 6) + (left >> 2));
        }
    }

    /// <summary>覆盖区/参数 → 配置签名（改动即整帧缓存失效）。</summary>
    public static long ConfigSignatureOf(
        in SurfaceLightParameters p,
        float coverageX, float coverageY, float coverageWidth, float coverageHeight)
    {
        var seed = 17L;
        seed = Mix(seed, (long)(p.SunAzimuthDegrees * 1000f));
        seed = Mix(seed, (long)(p.SunElevationDegrees * 1000f));
        seed = Mix(seed, (long)(p.SunIntensity * 1000f));
        seed = Mix(seed, (long)(p.Ambient * 1000f));
        seed = Mix(seed, (long)(p.Warmth * 1000f));
        seed = Mix(seed, p.TintArgb);
        seed = Mix(seed, (long)(p.AxisXx * 1000f) ^ (long)(p.AxisYy * 1000f));
        seed = Mix(seed, (long)(p.NormalBias * 1000f));
        seed = Mix(seed, (long)(p.LocalDeviation * 1000f));
        seed = Mix(seed, (long)(p.SpecularExponent * 100f));
        seed = Mix(seed, (long)(p.TintTransfer * 1000f));
        seed = Mix(seed, (long)(coverageX * 10f));
        seed = Mix(seed, (long)(coverageY * 10f));
        seed = Mix(seed, (long)(coverageWidth * 10f));
        seed = Mix(seed, (long)(coverageHeight * 10f));
        return seed;
    }
}
