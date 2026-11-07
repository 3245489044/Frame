#nullable enable

using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using FrontEndDriven.Diagnostics;
using Share.Conversation.Rendering.Surface;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 属性通道投影器（**唯一 Control.BackColor 写点**）：把已解析材质投到控件属性面。
/// <para>属性通道不能承载 alpha（<c>Control.BackColor</c> 对 A&lt;255 抛异常）；本投影器负责把材质底色
/// 不透明化（叠到父级不透明底），并复用 <see cref="SurfaceColorDomain"/> / <see cref="SurfaceColorComposition"/> 单点实现。</para>
/// <para>描边色取材质自身的 <c>stroke</c> 层（声明唯一来源）；无声明才保留旧派生色（零回归）。</para>
/// </summary>
public static class WinFormsPropertyProjector
{
    /// <summary>通道 id（与声明面 caps 键前缀一致）。</summary>
    public const string ChannelId = "property";

    private static readonly ConditionalWeakTable<Control, object> StrokeDegradationLogged = new();
    private static readonly object StrokeDegradationMarker = new();

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
        var framed = control is ButtonBase or ComboBox;
        var stroke = DeclaredStroke(material);
        if (!needsBackColor && !needsForeColor && !framed) return true;

        if (needsBackColor) control.BackColor = baseColor;
        if (needsForeColor) control.ForeColor = contrast;

        ApplyStroke(control, material, baseColor, stroke);
        return true;
    }

    /// <summary>
    /// 描边落属性面：可表达的组合控件（Button/CheckBox/ComboBox）取声明色；不可表达者显式记降级（禁伪造）。
    /// <para>只改颜色，不改 <c>BorderSize</c> / 尺寸 / Dock（描边几何不在本批）。</para>
    /// </summary>
    private static void ApplyStroke(Control control, ResolvedMaterial material, Color baseColor, Color? stroke)
    {
        var border = stroke ?? ControlPaint.Dark(baseColor, 0.15f);
        switch (control)
        {
            case ButtonBase buttonBase when buttonBase.FlatStyle == FlatStyle.Standard:
                buttonBase.FlatStyle = FlatStyle.Flat;
                buttonBase.FlatAppearance.BorderColor = border;
                return;
            case ComboBox combo:
                // 系统扁平边框不接受颜色；语义保留「可点击面」，不伪造描边。
                combo.FlatStyle = FlatStyle.Flat;
                LogUnavailable(control, material, stroke);
                return;
            case ListBox:
                LogUnavailable(control, material, stroke);
                return;
            default:
                return;
        }
    }

    /// <summary>材质声明的描边色（首个 <c>stroke</c> 层；未声明/不可解析 ⇒ null）。</summary>
    /// <param name="material">已解析材质。</param>
    public static Color? DeclaredStroke(ResolvedMaterial material)
    {
        foreach (var layerSpec in ResolvedLayerSpecProjection.Parse(material.Layers).Specs)
        {
            if (layerSpec.Kind != LayerPrimitive.Stroke || !layerSpec.HasColor) continue;
            return Color.FromArgb(layerSpec.ColorArgb);
        }

        return null;
    }

    /// <summary>无法表达描边的一次性降级记账（每控件一条，防日志噪声）。</summary>
    private static void LogUnavailable(Control control, ResolvedMaterial material, Color? stroke)
    {
        if (stroke is null) return; // 无声明 = 旧行为，不算降级
        if (StrokeDegradationLogged.TryGetValue(control, out _)) return;
        StrokeDegradationLogged.Add(control, StrokeDegradationMarker);
        FrontEndDrivenLog.Log("SkinDecorator_property_stroke_unavailable", new
        {
            type = control.GetType().Name,
            material = material.MaterialId,
            stroke = stroke.Value.ToArgb(),
            reason = "control-cannot-express-stroke",
        });
    }

    /// <summary>材质底色 → 属性通道可承载的不透明 ARGB（首个 fill/gradient 层）。</summary>
    /// <param name="control">目标控件（提供父级不透明底）。</param>
    /// <param name="material">已解析材质。</param>
    /// <param name="argb">不透明底色（A=255）。</param>
    public static bool TryGetOpaqueBase(Control control, ResolvedMaterial material, out int argb)
    {
        argb = 0;
        foreach (var layerSpec in ResolvedLayerSpecProjection.Parse(material.Layers).Specs)
        {
            if (layerSpec.Kind is not (LayerPrimitive.Fill or LayerPrimitive.Gradient) || !layerSpec.HasColor) continue;

            var foreground = layerSpec.ColorArgb;
            if (((foreground >> 24) & 0xFF) == 0) continue;

            argb = SurfaceColorComposition.Over(foreground, OpaqueBehind(control));
            return true;
        }

        return false;
    }

    /// <summary>
    /// 父链首个**显式上色**的不透明底色；系统默认/继承背景视为「未上色」，一路向上。
    /// 全链未上色 ⇒ 页面底色（声明缺失时仍回落 <see cref="SystemColors.Control"/>，零回归）。
    /// </summary>
    private static int OpaqueBehind(Control control)
    {
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            var background = parent.BackColor;
            if (background.IsEmpty) continue;
            if (background.ToArgb() == SystemColors.Control.ToArgb()) continue;
            if (background.A == 255) return background.ToArgb();
        }

        return SkinMaterialCatalog.PageBackground?.ToArgb() ?? SystemColors.Control.ToArgb();
    }

    /// <summary>反差文字色（按底色亮度在黑/白之间二选一）。</summary>
    private static Color Contrast(Color background)
    {
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        return luminance < 0.55 ? Color.FromArgb(230, 237, 243) : Color.FromArgb(30, 34, 40);
    }
}
