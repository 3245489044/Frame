#nullable enable

using System.Drawing;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 色值 → GDI+ 颜色（**WinForms 侧唯一桥**）：hex/rgb()/rgba()/transparent/none 走 <see cref="SurfaceColor"/>，
/// 命名色由 <see cref="Color.FromName"/> 兜底（`Plate.Surface` 为 BCL-only，不引 System.Drawing）。
/// <para>绘制通道与属性通道共用本实现，禁各自再写一套字符串解析。</para>
/// </summary>
internal static class SurfaceColorParse
{
    /// <summary>解析色值文本（失败 ⇒ false，调用方记账，禁抛给渲染路径）。</summary>
    /// <param name="value">色值文本。</param>
    /// <param name="color">GDI+ 颜色。</param>
    public static bool TryParse(string? value, out Color color)
    {
        color = Color.Transparent;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var text = value.Trim();
        if (SurfaceColor.TryParseColor(text, out var argb))
        {
            color = Color.FromArgb(argb);
            return true;
        }

        if (Color.FromName(text) is { IsKnownColor: true } named)
        {
            color = named;
            return true;
        }

        return false;
    }

    /// <summary>解析色值文本为打包 ARGB（失败 ⇒ false）。</summary>
    /// <param name="value">色值文本。</param>
    /// <param name="argb">打包 ARGB（<c>0xAARRGGBB</c>）。</param>
    public static bool TryParseArgb(string? value, out int argb)
    {
        if (TryParse(value, out var color))
        {
            argb = color.ToArgb();
            return true;
        }

        argb = 0;
        return false;
    }
}
