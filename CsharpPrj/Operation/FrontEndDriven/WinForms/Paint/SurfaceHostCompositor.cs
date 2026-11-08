#nullable enable

using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Modules.CapabilityResolution;
using Share.Conversation.Rendering.Surface;
using Share.SurfacePaint;
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
    // 每控件缓存上次 revision（颜色/DPI/尺寸未变 ⇒ 复用，避免重复摘要计算）。
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
        // 填充取值单源 = SurfaceMaterialFace（与页底色/属性通道同一口径：fill|gradient、A>0 叠底）。
        if (!SurfaceMaterialFace.TryGetOpaqueFill(material, OpaqueBehind(control), out var argb)) return false;

        face = new SurfaceFace(argb, material.MaterialId, material.FallbackStep, RevisionOf(control));
        return true;
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

    /// <summary>宿主链底色/DPI 摘要（颜色或 DPI/尺寸变化 ⇒ revision 变；未变化 ⇒ 复用上次值）。</summary>
    /// <param name="control">目标控件。</param>
    public static int RevisionOf(Control control)
    {
        var box = Revisions.GetValue(control, static _ => new RevisionBox());
        var behind = OpaqueBehind(control);
        var dpi = control.DeviceDpi;
        var width = control.Width;
        var height = control.Height;
        if (box.Valid && box.Behind == behind && box.Dpi == dpi && box.Width == width && box.Height == height)
        {
            return box.Value;
        }

        box.Behind = behind;
        box.Dpi = dpi;
        box.Width = width;
        box.Height = height;
        box.Value = HashCode.Combine(behind, dpi, width, height);
        box.Valid = true;
        return box.Value;
    }

    private sealed class RevisionBox
    {
        public int Value { get; set; } = -1;
        public int Behind { get; set; }
        public int Dpi { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Valid { get; set; }
    }
}
