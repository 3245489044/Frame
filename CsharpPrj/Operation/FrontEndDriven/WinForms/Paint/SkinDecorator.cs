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

        var material = descriptor.SkinRefs is { Count: > 0 }
            ? SelectMaterial(descriptor.SkinRefs)
            : SelectMaterial(SkinMaterialCatalog.For(descriptor.Id) ?? SkinMaterialCatalog.For(descriptor.Name))
              ?? SkinMaterialCatalog.Global;
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

        // 解析期问题可见化（回退/不支持/模拟；禁静默）。
        if (descriptor.Issues is { Count: > 0 } issues)
        {
            FrontEndDrivenLog.Log("SkinDecorator_issues", new
            {
                type = control.GetType().Name,
                material = material.MaterialId,
                issues = string.Join(";", issues.Take(4).Select(issue => issue.Code + ":" + issue.Detail)),
            });
        }

        state.Material = material;
        state.MaterialKey = key;
        if (state.Handler is null)
        {
            // 首挂：双缓冲失败只记账不抛（绘制仍正确，仅闪烁风险，禁静默）。
            if (!SkinDoubleBuffer.TryEnable(control, out var doubleBufferReason) && doubleBufferReason is not null)
            {
                FrontEndDrivenLog.Log("SkinDecorator_doublebuffer_unavailable",
                    new { type = control.GetType().Name, reason = doubleBufferReason });
            }

            var target = control;
            state.Handler = (_, e) => PaintControl(target, state, e);
            control.Paint += state.Handler;
            control.Disposed += (_, _) => Detach(target);
            SkinDiagnostics.RecordAttach(true);
        }

        control.Invalidate();
    }

    /// <summary>关闭 owner-draw 并卸除全部已挂绘制（开关关闭 ⇒ 回到纯属性着色，零残留）。</summary>
    public static void Disable()
    {
        Enabled = false;
        // 先快照：Detach 会从 States 中移除条目，直接枚举会破坏表。
        var attached = new List<Control>();
        foreach (var pair in States) attached.Add(pair.Key);
        foreach (var control in attached) Detach(control);
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
        SkinDiagnostics.RecordAttach(false);
    }

    /// <summary>控件当前生效材质（未挂载 ⇒ null；断言/诊断读取面）。</summary>
    /// <param name="control">目标控件。</param>
    public static ResolvedMaterial? MaterialOf(Control? control)
        => control is not null && States.TryGetValue(control, out var state) ? state.Material : null;

    /// <summary>是否属于 owner-draw 白名单（断言前置：白名单外永远不着色）。</summary>
    /// <param name="control">目标控件。</param>
    public static bool IsOwnerDrawSupported(Control? control)
        => control is not null && SupportsOwnerDraw(control);

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
        SkinDiagnostics.RecordPaint(skipped);
        if (skipped.Count > 0)
        {
            FrontEndDrivenLog.Log("SkinDecorator_skipped", new { material = material.MaterialId, skipped = string.Join(";", skipped) });
        }

        return bitmap;
    }

    /// <summary>槽优先选择：<c>*_surface</c>（背景槽）→ 其余按声明序（禁以枚举顺序当语义）。</summary>
    private static ResolvedMaterial? SelectMaterial(IReadOnlyDictionary<string, ResolvedMaterial>? materials)
    {
        if (materials is not { Count: > 0 }) return null;

        foreach (var pair in materials)
        {
            if (pair.Key.EndsWith("_surface", StringComparison.Ordinal)) return pair.Value;
        }

        return materials.Values.FirstOrDefault();
    }

    private static string CacheKey(ResolvedMaterial material, Rectangle bounds, int dpi)
        => $"{MaterialKey(material)}:{bounds.Width}x{bounds.Height}@dpi{dpi}";

    /// <summary>材质指纹：含层参数摘要（同材质换色必须失效，否则换肤永不重绘）。</summary>
    private static string MaterialKey(ResolvedMaterial material)
        => material.MaterialId + "|" + (material.FallbackUsed ?? string.Empty) + "|" + ParamDigest(material);

    /// <summary>层参数摘要（角色/图元/z/全部参数；参数按键序，禁枚举顺序当语义）。</summary>
    private static string ParamDigest(ResolvedMaterial material)
        => string.Join("/", material.Layers.Select(layer =>
            layer.Role + ":" + layer.Primitive + ":" + layer.Z + ":" +
            string.Join(",", layer.Params.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + "=" + pair.Value))));

    private sealed class SkinState
    {
        internal ResolvedMaterial? Material { get; set; }
        internal string? MaterialKey { get; set; }
        internal PaintEventHandler? Handler { get; set; }
        internal Bitmap? Cache { get; set; }
        internal string? CacheKey { get; set; }
    }
}
