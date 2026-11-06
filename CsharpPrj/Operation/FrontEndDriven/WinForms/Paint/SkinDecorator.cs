#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.WinForms.Applier;
using Share.Conversation.Rendering.Surface;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 皮肤装饰器（owner-draw 接线）：按 <see cref="ControlDescriptor.SkinRefs"/> 取层栈绘制。
/// <para>缓存键 = 材质指纹 + 尺寸档 + DPI 档（DPI 变化即失效，防缩放后位图糊）。</para>
/// <para>默认关闭（<see cref="Enabled"/>=false）：未接线时回到纯属性着色，零行为变化。</para>
/// </summary>
public static class SkinDecorator
{
    /// <summary>是否启用 owner-draw（M5b 回退开关；默认关闭）。</summary>
    public static bool Enabled { get; set; }

    private static readonly ConditionalWeakTable<Control, SkinState> States = new();

    /// <summary>按描述符挂载/更新绘制（幂等；未启用/无材质/不支持 ⇒ 卸除，禁残留旧绘制）。</summary>
    public static void Attach(Control? control, ControlDescriptor descriptor)
    {
        if (control is null) return;
        if (!Enabled)
        {
            Detach(control);
            return;
        }

        var material = descriptor.SkinRefs is { Count: > 0 } refs ? FirstMaterial(refs) : null;
        if (material is null)
        {
            Detach(control);
            return;
        }

        // 只对「背景由父绘制」的容器类控件启用；其余控件自绘内容，挂 Paint 只会白耗缓存（可见记账）。
        if (!SupportsOwnerDraw(control))
        {
            Detach(control);
            FrontEndDrivenLog.Log("SkinDecorator_unsupported", new { type = control.GetType().Name, material = material.MaterialId });
            return;
        }

        var state = States.GetValue(control, static _ => new SkinState());
        var key = MaterialKey(material);
        if (string.Equals(state.MaterialKey, key, StringComparison.Ordinal))
        {
            return; // 同材质：不重挂、不重绘
        }

        state.Material = material;
        state.MaterialKey = key;
        if (state.Handler is null)
        {
            var target = control;
            state.Handler = (_, e) => PaintControl(target, state, e);
            control.Paint += state.Handler;
            control.Disposed += (_, _) => Detach(target);
        }

        control.Invalidate();
    }

    /// <summary>卸除绘制（控件复用/材质消失/开关关闭；幂等）。</summary>
    public static void Detach(Control? control)
    {
        if (control is null || !States.TryGetValue(control, out var state)) return;
        if (state.Handler is not null) control.Paint -= state.Handler;
        state.Handler = null;
        state.Cache = null;
        state.Material = null;
        state.MaterialKey = null;
        States.Remove(control);
    }

    /// <summary>是否支持 owner-draw（容器/文本承载类；其余控件自绘内容）。</summary>
    private static bool SupportsOwnerDraw(Control control)
        => control is Panel or FlowLayoutPanel or TableLayoutPanel or UserControl or GroupBox or Label;

    private static void PaintControl(Control control, SkinState state, PaintEventArgs e)
    {
        var material = state.Material;
        if (material is null) return;

        var bounds = new Rectangle(Point.Empty, control.ClientSize);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var key = CacheKey(material, bounds, control.DeviceDpi);
        if (state.Cache is null || state.CacheKey != key)
        {
            state.Cache?.Dispose();
            state.Cache = Render(material, bounds);
            state.CacheKey = key;
        }

        if (state.Cache is not null) e.Graphics.DrawImageUnscaled(state.Cache, bounds);
    }

    private static Bitmap Render(ResolvedMaterial material, Rectangle bounds)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        var skipped = SkinPaintEngine.Paint(graphics, new Rectangle(Point.Empty, bounds.Size), material);
        if (skipped.Count > 0)
        {
            FrontEndDrivenLog.Log("SkinDecorator_skipped", new { material = material.MaterialId, skipped = string.Join(";", skipped) });
        }

        return bitmap;
    }

    private static ResolvedMaterial? FirstMaterial(IReadOnlyDictionary<string, ResolvedMaterial> materials)
        => materials.Values.FirstOrDefault();

    private static string CacheKey(ResolvedMaterial material, Rectangle bounds, int dpi)
    {
        var layers = string.Join("/", material.Layers.Select(layer =>
            layer.Primitive + ":" + layer.Role + ":" + layer.Z));
        return $"{material.MaterialId}:{material.FallbackUsed}:{layers}:{bounds.Width}x{bounds.Height}@dpi{dpi}";
    }

    /// <summary>材质指纹（不含尺寸/DPI：尺寸与 DPI 已进渲染缓存键）。</summary>
    private static string MaterialKey(ResolvedMaterial material)
        => material.MaterialId + "|" + (material.FallbackUsed ?? string.Empty) + "|" + material.Layers.Count;

    private sealed class SkinState
    {
        internal ResolvedMaterial? Material { get; set; }
        internal string? MaterialKey { get; set; }
        internal PaintEventHandler? Handler { get; set; }
        internal Bitmap? Cache { get; set; }
        internal string? CacheKey { get; set; }
    }
}
