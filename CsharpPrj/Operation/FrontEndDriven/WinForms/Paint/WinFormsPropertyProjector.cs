#nullable enable

using System;
using System.Drawing;
using System.Windows.Forms;
using Share.Conversation.Rendering.Surface;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 属性通道投影器（**唯一 Control.BackColor 写点**）：把已解析材质投到控件属性面。
/// <para>属性通道不能承载 alpha（<c>Control.BackColor</c> 对 A&lt;255 抛异常）；本投影器负责把材质底色
/// 不透明化（叠到父级不透明底），并复用 <see cref="SurfaceColorDomain"/> / <see cref="SurfaceColorComposition"/> 单点实现。</para>
/// </summary>
public static class WinFormsPropertyProjector
{
    /// <summary>通道 id（与声明面 caps 键前缀一致）。</summary>
    public const string ChannelId = "property";

    /// <summary>把材质底色投到控件属性面（不抛；不可投影 ⇒ false + 原因）。</summary>
    /// <param name="control">目标控件。</param>
    /// <param name="material">已解析材质。</param>
    /// <param name="reason">不可投影原因（可空）。</param>
    public static bool TryApply(Control? control, ResolvedMaterial? material, out string? reason)
    {
        reason = null;
        if (control is null || material is null) { reason = "no-material"; return false; }

        if (!TryGetOpaqueBase(control, material, out var baseArgb))
        {
            reason = "no-opaque-base";
            return false;
        }

        var baseColor = Color.FromArgb(baseArgb);
        var contrast = Contrast(baseColor);
        var needsBackColor = control.BackColor.ToArgb() != baseArgb;
        var needsForeColor = control.ForeColor.ToArgb() != contrast.ToArgb();
        var needsButton = control is Button { FlatStyle: FlatStyle.Standard };
        if (!needsBackColor && !needsForeColor && !needsButton) return true;

        if (needsBackColor) control.BackColor = baseColor;
        if (needsForeColor) control.ForeColor = contrast;

        if (control is Button button && button.FlatStyle == FlatStyle.Standard)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = ControlPaint.Dark(baseColor, 0.15f);
        }

        return true;
    }

    /// <summary>材质底色 → 属性通道可承载的不透明 ARGB（首个 fill/gradient 层）。</summary>
    /// <param name="control">目标控件（提供父级不透明底）。</param>
    /// <param name="material">已解析材质。</param>
    /// <param name="argb">不透明底色（A=255）。</param>
    public static bool TryGetOpaqueBase(Control control, ResolvedMaterial material, out int argb)
    {
        argb = 0;
        foreach (var layer in material.Layers)
        {
            if (!string.Equals(layer.Primitive, "fill", StringComparison.Ordinal)
                && !string.Equals(layer.Primitive, "gradient", StringComparison.Ordinal))
            {
                continue;
            }

            if (!layer.Params.TryGetValue("value", out var value)) continue;

            var first = layer.Primitive == "gradient"
                ? value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
                : value;
            if (string.IsNullOrWhiteSpace(first) || !TryParseColor(first, out var foreground)) continue;
            if (((foreground >> 24) & 0xFF) == 0) continue;

            argb = SurfaceColorComposition.Over(foreground, OpaqueBehind(control));
            return true;
        }

        return false;
    }

    /// <summary>父链首个不透明底色（无父 ⇒ 系统窗体色）。</summary>
    private static int OpaqueBehind(Control control)
    {
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.BackColor.A == 255) return parent.BackColor.ToArgb();
        }

        return SystemColors.Control.ToArgb();
    }

    /// <summary>色值解析（唯一桥：<see cref="SurfaceColorParse"/>；失败 ⇒ false）。</summary>
    private static bool TryParseColor(string value, out int argb) => SurfaceColorParse.TryParseArgb(value, out argb);

    /// <summary>反差文字色（按底色亮度在黑/白之间二选一）。</summary>
    private static Color Contrast(Color background)
    {
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        return luminance < 0.55 ? Color.FromArgb(230, 237, 243) : Color.FromArgb(30, 34, 40);
    }
}
