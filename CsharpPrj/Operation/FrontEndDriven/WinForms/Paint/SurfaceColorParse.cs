#nullable enable

using System.Drawing;
using Share.SurfacePaint;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>色值桥门面（薄层）：实现单源 = <see cref="SurfaceColorParser"/>（Share.SurfacePaint）。</summary>
internal static class SurfaceColorParse
{
    public static bool TryParse(string? value, out Color color) => SurfaceColorParser.TryParse(value, out color);

    public static bool TryParseArgb(string? value, out int argb) => SurfaceColorParser.TryParseArgb(value, out argb);
}