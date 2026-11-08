#nullable enable

using System.Drawing;
using UiBackend;

namespace Share.SurfacePaint;

/// <summary>
/// 色值 → GDI+ 颜色（**WinForms 侧唯一桥**）：hex/rgb()/rgba()/transparent/none/命名色全部走 <see cref="SurfaceColor"/>。
/// <para>禁 <see cref="Color.FromName"/>：它会放行未换值 token 且系统色表与 CSS 不一致；本桥只做 <c>Color.FromArgb</c> 装箱。</para>
/// <para>绘制通道与属性通道共用本实现，禁各自再写一套字符串解析。</para>
/// </summary>
public static class SurfaceColorParser
{
    /// <summary>解析色值文本（失败 ⇒ false，调用方记账，禁抛给渲染路径）。</summary>
    /// <param name="value">色值文本。</param>
    /// <param name="color">GDI+ 颜色。</param>
    public static bool TryParse(string? value, out Color color)
    {
        color = Color.Transparent;
        if (string.IsNullOrWhiteSpace(value)) return false;

        if (SurfaceColor.TryParseColor(value.Trim(), out var argb))
        {
            color = Color.FromArgb(argb);
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
