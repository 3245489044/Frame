#nullable enable

using System;

namespace MathematicalAlgorithms.Lighting;

/// <summary>
/// C# ↔ HLSL 着色数值对拍（**照明段**）：HLSL <c>ShadeColor</c> 的公式镜像。
/// <para>不复制光数学：太阳方向/归一化/点积一律复用 <see cref="SurfaceLightSolver"/>；
/// 只镜像 GPU 的逐像素组合顺序，容差见 <see cref="Tolerance"/>。</para>
/// <para>暖度/环境色的 HLSL 后处理（<c>lerp(lit, Tint, Warmth*0.35)</c>）与 GDI+ 的
/// <see cref="SurfaceShadingMath.LitColor"/> 语义不同，属已知不对称（P4/P6 收口），不在本对拍范围。</para>
/// </summary>
public static class SurfaceShadingParity
{
    /// <summary>对拍容差（绝对误差）。</summary>
    public const float Tolerance = 1e-4f;

    /// <summary>HLSL <c>ShadeColor</c> 的照明段镜像（base × (ambient + sun·N·L) + spec^exp × gain）。</summary>
    public static (float R, float G, float B) ShadeLighting(
        in SurfaceLightParameters p,
        float baseR, float baseG, float baseB,
        float normalX, float normalY, float normalZ)
    {
        var sun = SurfaceLightSolver.SunDirection(p.SunAzimuthDegrees, p.SunElevationDegrees);
        var (nx, ny, nz) = SurfaceLightSolver.Normalize(
            normalX, normalY, MathF.Max(1e-3f, normalZ));
        var half = SurfaceLightSolver.Normalize(sun.X, sun.Y, sun.Z + 1f);

        var diffuse = MathF.Max(0f, SurfaceLightSolver.Dot((nx, ny, nz), sun));
        var specular = MathF.Pow(
            MathF.Max(0f, SurfaceLightSolver.Dot((nx, ny, nz), half)),
            MathF.Max(1f, p.SpecularExponent));
        var light = Math.Clamp(p.Ambient + p.SunIntensity * diffuse, 0f, 2f);

        return (
            Math.Clamp(baseR * light + specular * SurfaceShadingMath.SpecularGain, 0f, 1f),
            Math.Clamp(baseG * light + specular * SurfaceShadingMath.SpecularGain, 0f, 1f),
            Math.Clamp(baseB * light + specular * SurfaceShadingMath.SpecularGain, 0f, 1f));
    }
}
