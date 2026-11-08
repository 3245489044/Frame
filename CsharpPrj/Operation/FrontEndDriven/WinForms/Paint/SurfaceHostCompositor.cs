#nullable enable

using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Modules.CapabilityResolution;
using Share.Conversation.Rendering.Surface;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 宿主合成面（本批只做 A=255 的实心玻璃面）：fill 叠到父链不透明底。
/// <para>属性通道不能承载 alpha，所有 alpha 必须在这里单点落成 A=255；绘制通道不经过本类。</para>
/// </summary>
/// <param name="Argb">不透明 ARGB。</param>
/// <param name="SourceMaterial">来源材质 id。</param>
/// <param name="Step">回落梯度步。</param>
/// <param name="Revision">宿主链底色/DPI 摘要（变更即失效）。</param>
public sealed record SurfaceFace(
    int Argb,
    string SourceMaterial,
    SurfaceFallbackStep Step,
    int Revision);

/// <summary>
/// 宿主合成器：属性通道的唯一“底色 → 不透明面”实现。
/// <para>父链首个显式上色的不透明底；系统默认/继承背景视为未上色，一路向上到页面底色。</para>
/// </summary>
public static class SurfaceHostCompositor
{
    // 注意：Value 虽只写不读，但已被 PublicApiAdditive 基线收录（public 声明删除即红），故保留。
    private static readonly ConditionalWeakTable<Control, RevisionBox> Revisions = new();

    /// <summary>
    /// 「未上色」背景判据：空背景或系统默认 <see cref="SystemColors.Control"/>。
    /// <para>属性通道（<see cref="OpaqueBehind"/>）与绘制通道快照（<c>SkinDecorator</c>）共用同一判据。</para>
    /// </summary>
    /// <param name="background">父级背景色。</param>
    internal static bool IsUnstyled(Color background)
        => background.IsEmpty || background.ToArgb() == SystemColors.Control.ToArgb();

    /// <summary>材质 → 不透明合成面（无 fill/gradient ⇒ false）。</summary>
    /// <param name="control">目标控件（提供父链底色）。</param>
    /// <param name="material">已解析材质。</param>
    /// <param name="face">合成面。</param>
    public static bool TrySolidFace(Control control, ResolvedMaterial material, out SurfaceFace face)
    {
        face = default!;
        foreach (var layerSpec in ResolvedLayerSpecProjection.ParseCached(material.Layers).Specs)
        {
            if (layerSpec.Kind is not (LayerPrimitive.Fill or LayerPrimitive.Gradient) || !layerSpec.HasColor) continue;

            var foreground = layerSpec.ColorArgb;
            if (((foreground >> 24) & 0xFF) == 0) continue;

            face = new SurfaceFace(
                SurfaceColorComposition.Over(foreground, OpaqueBehind(control)),
                material.MaterialId,
                material.FallbackStep,
                RevisionOf(control));
            return true;
        }

        return false;
    }

    /// <summary>
    /// 父链首个**显式上色**的不透明底色；系统默认/继承背景视为「未上色」，一路向上。
    /// 全链未上色 ⇒ 页面底色（声明缺失时仍回落 <see cref="SystemColors.Control"/>，零回归）。
    /// </summary>
    /// <param name="control">目标控件。</param>
    public static int OpaqueBehind(Control control)
    {
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            var background = parent.BackColor;
            if (IsUnstyled(background)) continue;
            if (background.A == 255) return background.ToArgb();
        }

        return SkinMaterialCatalog.PageBackground?.ToArgb() ?? SystemColors.Control.ToArgb();
    }

    /// <summary>宿主链底色/DPI 摘要（颜色或 DPI 变化 ⇒ revision 变）。</summary>
    /// <param name="control">目标控件。</param>
    public static int RevisionOf(Control control)
    {
        var box = Revisions.GetValue(control, static _ => new RevisionBox());
        var behind = OpaqueBehind(control);
        var revision = HashCode.Combine(behind, control.DeviceDpi, control.Width, control.Height);
        box.Value = revision;
        return revision;
    }

    private sealed class RevisionBox
    {
        public int Value { get; set; } = -1;
    }
}
