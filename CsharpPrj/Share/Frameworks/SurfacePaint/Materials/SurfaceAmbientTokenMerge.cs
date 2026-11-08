#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Share.SurfacePaint.Materials;

/// <summary>
/// 页场令牌覆盖项（用户态覆盖的载体无关视图）。null = 保持声明值。
/// </summary>
public readonly record struct SurfaceAmbientOverride(
    bool? AmbientEnabled,
    float? RadialAlpha,
    float? EdgeWidth);

/// <summary>
/// 页场令牌覆盖（唯一实现）：用户态覆盖项 → 生效令牌表。
/// <para>吸收原 <c>SurfaceQualitySettings.MergeTokens</c>（ERR-08 的令牌覆盖链单源）；
/// 存储读取仍在载体侧，本类只做纯函数合并。</para>
/// </summary>
public static class SurfaceAmbientTokenMerge
{
    /// <summary>按覆盖项改写令牌表（只覆盖已设置的项；其余保持声明值）。</summary>
    /// <param name="declared">声明令牌表。</param>
    /// <param name="overrides">用户态覆盖项。</param>
    public static Dictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> declared, SurfaceAmbientOverride overrides)
    {
        var tokens = new Dictionary<string, string>(declared, StringComparer.OrdinalIgnoreCase);

        if (overrides.AmbientEnabled is { } ambient)
            tokens["ambient_enabled"] = ambient ? "1" : "0";
        if (overrides.RadialAlpha is { } radial)
            tokens["ambient_radial_alpha"] = radial.ToString("0.###", CultureInfo.InvariantCulture);
        if (overrides.EdgeWidth is { } edge)
            tokens["ambient_edge_width"] = edge.ToString("0.###", CultureInfo.InvariantCulture);

        return tokens;
    }
}
