#nullable enable

using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using FrontEndDriven.Diagnostics;
using Share.Conversation.Rendering.Surface;
using Share.SurfacePaint;
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
        => TryApply(control, material, tintArgb: null, out reason);

    /// <summary>把材质底色投到控件属性面（可选旧样式 accent tint；不抛）。</summary>
    /// <param name="control">目标控件。</param>
    /// <param name="material">已解析材质。</param>
    /// <param name="tintArgb">C 类 accent 色（null = 无色）。</param>
    /// <param name="reason">不可投影原因（可空）。</param>
    public static bool TryApply(Control? control, ResolvedMaterial? material, int? tintArgb, out string? reason)
    {
        reason = null;
        if (control is null || material is null) { reason = "no-material"; return false; }

        if (!TryGetOpaqueBase(control, material, out var baseArgb))
        {
            reason = "no-opaque-base";
            return false;
        }

        if (tintArgb is { } tint)
        {
            baseArgb = SurfaceColorComposition.Over(tint, baseArgb);
        }

        var baseColor = Color.FromArgb(baseArgb);
        var contrast = Color.FromArgb(SurfaceForegroundPolicy.ForegroundFor(baseArgb));
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
            case Button button:
                // P1-6·按钮规格统一：去系统 3D 边（BorderSize=0）+ 材质色 ±8% 的 hover/pressed，
                // 最小高度 24 DIP（不再是 32 统一），避免「系统 3D 边 + 白岛」。
                ApplyButtonSpec(button, baseColor, border);
                return;
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

    /// <summary>
    /// 按钮统一规格（P1-6）：Flat + BorderSize=0 + 居中 + Padding(8,0) + 不画视觉样式背景；
    /// hover/pressed 取材质底色 ±8%，最小高度 24 DIP。幂等，属性应用可重复调用。
    /// </summary>
    private static void ApplyButtonSpec(Button button, Color baseColor, Color border)
    {
        if (button.FlatStyle != FlatStyle.Flat)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = border;
        }

        button.FlatAppearance.BorderSize = SurfaceButtonSpec.BorderSize;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.Padding = new Padding(SurfaceButtonSpec.PaddingXDip, 0, SurfaceButtonSpec.PaddingXDip, 0);
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(baseColor, SurfaceButtonSpec.HoverLighten);
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(baseColor, SurfaceButtonSpec.PressedDarken);

        var minHeight = (int)Math.Round(
            BorderUnits.DipsToPixels(SurfaceButtonSpec.MinHeightDip, button.DeviceDpi));
        if (button.MinimumSize.Height < minHeight)
            button.MinimumSize = new Size(button.MinimumSize.Width, minHeight);
    }

    /// <summary>材质声明的描边色（唯一口径 = <see cref="SurfaceMaterialFace.DeclaredStroke"/>：末条可见描边；无声明 ⇒ null）。</summary>
    /// <param name="material">已解析材质。</param>
    public static Color? DeclaredStroke(ResolvedMaterial material)
    {
        var stroke = SurfaceMaterialFace.DeclaredStroke(material);
        return stroke is null ? null : Color.FromArgb(stroke.Colors.Left);
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
        if (SurfaceHostCompositor.TrySolidFace(control, material, out var face))
        {
            argb = face.Argb;
            return true;
        }

        argb = 0;
        return false;
    }

    /// <summary>
    /// 给定面底色的可读前景色：实现单源 = <see cref="SurfaceForegroundPolicy"/>（WCAG 相对亮度 + 对比度比）。
    /// <para>属性通道与自绘外壳（C1）共用本入口，禁在载体侧再写第二份亮度公式。</para>
    /// </summary>
    /// <param name="background">面底色。</param>
    public static Color ForegroundFor(Color background)
        => Color.FromArgb(SurfaceForegroundPolicy.ForegroundFor(background.ToArgb()));

    /// <summary>给定材质与宿主控件的可读前景色（无法取不透明底 ⇒ 深色墨）。</summary>
    /// <param name="control">目标控件（提供父级不透明底）。</param>
    /// <param name="material">已解析材质。</param>
    public static Color ContrastOf(Control control, ResolvedMaterial material)
        => TryGetOpaqueBase(control, material, out var argb)
            ? Color.FromArgb(SurfaceForegroundPolicy.ForegroundFor(argb))
            : Color.FromArgb(SurfaceForegroundPolicy.DarkInkArgb);

    /// <summary>两色 WCAG 对比度比（判据用；1..21）。</summary>
    /// <param name="a">颜色 A。</param>
    /// <param name="b">颜色 B。</param>
    public static double ContrastRatio(Color a, Color b)
        => SurfaceForegroundPolicy.ContrastRatio(a.ToArgb(), b.ToArgb());
}
