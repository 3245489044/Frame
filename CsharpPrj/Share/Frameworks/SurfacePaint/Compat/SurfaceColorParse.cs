#nullable enable

using System.Drawing;
using Share.SurfacePaint;

namespace Share.SurfacePaint.Compat;

/// <summary>色值桥门面（薄层）：实现单源 = <see cref="SurfaceColorParser"/>。</summary>
public static class SurfaceColorParse
{
    /// <summary>解析为 <see cref="Color"/>。</summary>
    public static bool TryParse(string? value, out Color color) => SurfaceColorParser.TryParse(value, out color);

    /// <summary>解析为 ARGB。</summary>
    public static bool TryParseArgb(string? value, out int argb) => SurfaceColorParser.TryParseArgb(value, out argb);
}
