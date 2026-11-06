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

    /// <summary>属性级换肤的已应用指纹（幂等；避免每次 Apply 重复写颜色）。</summary>
    private static readonly ConditionalWeakTable<Control, string> FallbackThemes = new();

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

        // 白名单外控件（Button/TextBox/ListBox/TreeView/TabControl…）不能 owner-draw，
        // 但保持系统浅色会直接破坏整体玻璃观感 ⇒ 只做「属性级换肤」（底色/文字色，可逆重启）。
        if (!SupportsOwnerDraw(control))
        {
            var themed = ApplyFallbackTheme(control, material);
            Detach(control);
            // 只有「连属性级换肤也做不到」才记账（可承载的控件静默换色，避免每控件一条噪声）。
            if (!themed)
            {
                FrontEndDrivenLog.Log("SkinDecorator_unsupported", new { type = control.GetType().Name, material = material.MaterialId });
            }

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

        EnsureRefreshTimer(control, state, HasBackdrop(material));

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
        state.RefreshTimer?.Stop();
        state.RefreshTimer?.Dispose();
        state.RefreshTimer = null;
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

    /// <summary>
    /// 非 owner-draw 控件的属性级换肤：底 = 材质底色，文字 = 反差色；Button 去系统立体感。
    /// <para>只写颜色，不参与绘制缓存；同材质重复调用直接跳过。</para>
    /// </summary>
    private static bool ApplyFallbackTheme(Control control, ResolvedMaterial material)
    {
        if (!SkinPaintEngine.TryGetBaseColor(material, out var baseColor)) return false;

        var key = MaterialKey(material);
        if (FallbackThemes.TryGetValue(control, out var applied)
            && string.Equals(applied, key, StringComparison.Ordinal))
        {
            return true;
        }

        control.BackColor = baseColor;
        control.ForeColor = Contrast(baseColor);

        if (control is Button button && button.FlatStyle == FlatStyle.Standard)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = ControlPaint.Dark(baseColor, 0.15f);
        }

        FallbackThemes.Remove(control);
        FallbackThemes.Add(control, key);
        return true;
    }

    /// <summary>反差文字色（按底色亮度在黑/白之间二选一）。</summary>
    private static Color Contrast(Color background)
    {
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        return luminance < 0.55 ? Color.FromArgb(230, 237, 243) : Color.FromArgb(30, 34, 40);
    }

    private static void PaintControl(Control control, SkinState state, PaintEventArgs e)
    {
        // 生成 backdrop 快照时，本控件会被父级 DrawToBitmap 重入；此时留白（父级背景即「背后」），防递归。
        if (state.SuppressPaint) return;

        var material = state.Material;
        if (material is null) return;

        var bounds = new Rectangle(Point.Empty, control.ClientSize);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        // 只有玻璃材质（含 backdrop）接入运行时光；普通材质外观不变。
        var light = HasBackdrop(material) ? ResolveLight(control) : (SurfaceLightState?)null;
        var key = CacheKey(material, bounds, control.DeviceDpi, light);
        if (state.Cache is null || state.CacheKey != key)
        {
            state.Cache?.Dispose();
            state.Cache = Render(material, bounds, control, state, light);
            state.CacheKey = key;
        }

        if (state.Cache is not null) e.Graphics.DrawImageUnscaled(state.Cache, bounds);
    }

    private static Bitmap Render(
        ResolvedMaterial material, Rectangle bounds, Control control, SkinState state, SurfaceLightState? light)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var backdrop = TryCaptureBehind(control, bounds, state);
        var skipped = SkinPaintEngine.Paint(graphics, new Rectangle(Point.Empty, bounds.Size), material, backdrop, light);
        SkinDiagnostics.RecordPaint(skipped);
        if (skipped.Count > 0)
        {
            FrontEndDrivenLog.Log("SkinDecorator_skipped", new { material = material.MaterialId, skipped = string.Join(";", skipped) });
        }

        return bitmap;
    }

    /// <summary>运行时光：时间按分钟量化、光标按 8px 量化（缓存键只在肉眼可辨变化时失效）。</summary>
    private static SurfaceLightState ResolveLight(Control control)
    {
        var width = control.ClientSize.Width;
        var cursor = control.PointToClient(Cursor.Position);
        var cursorX = Math.Clamp((cursor.X / 8) * 8, 0, Math.Max(0, width));
        var now = DateTime.Now;
        return SurfaceLight.Resolve(new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0), cursorX, width);
    }

    private static bool HasBackdrop(ResolvedMaterial material)
        => material.Layers.Any(layer => layer.Primitive == "backdrop");

    /// <summary>玻璃材质每秒失效一次缓存（时间/光标进缓存键，静态 Fingerprint 不变）。</summary>
    private static void EnsureRefreshTimer(Control control, SkinState state, bool enabled)
    {
        if (!enabled)
        {
            state.RefreshTimer?.Stop();
            return;
        }

        if (state.RefreshTimer is not null) return;

        var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += (_, _) => control.Invalidate();
        timer.Start();
        state.RefreshTimer = timer;
    }

    /// <summary>
    /// 面板背后的快照来源（不新增合成抽象）：让父级把自身内容画到位图；本控件在快照期间留白，
    /// 于是裁出的就是「本控件位置下方的父级背景 + 兄弟内容」。
    /// <para>边界：不穿越窗口/WebView2 HWND ⇒ 不做元素级看穿桌面（与 01 文档可达上限一致）。</para>
    /// </summary>
    private static Image? TryCaptureBehind(Control control, Rectangle bounds, SkinState state)
    {
        if (_capturingBackdrop) return null; // 父级也挂玻璃材质时，避免逐层向上递归

        var parent = control.Parent;
        if (parent is null || !parent.IsHandleCreated) return null;

        var parentSize = parent.ClientSize;
        if (parentSize.Width <= 0 || parentSize.Height <= 0) return null;

        var target = new Rectangle(control.Location, bounds.Size);
        var source = Rectangle.Intersect(target, new Rectangle(Point.Empty, parentSize));
        if (source.Width <= 0 || source.Height <= 0) return null;

        Bitmap? parentBitmap = null;
        _capturingBackdrop = true;
        state.SuppressPaint = true;
        try
        {
            parentBitmap = new Bitmap(parentSize.Width, parentSize.Height);
            parent.DrawToBitmap(parentBitmap, new Rectangle(Point.Empty, parentSize));

            var crop = new Bitmap(bounds.Width, bounds.Height);
            using (var graphics = Graphics.FromImage(crop))
            {
                graphics.DrawImage(
                    parentBitmap,
                    new Rectangle(source.X - target.X, source.Y - target.Y, source.Width, source.Height),
                    source, GraphicsUnit.Pixel);
            }

            return crop;
        }
        catch (Exception ex)
        {
            FrontEndDrivenLog.Log("SkinDecorator_backdrop_snapshot_failed", new { message = ex.Message });
            return null;
        }
        finally
        {
            parentBitmap?.Dispose();
            state.SuppressPaint = false;
            _capturingBackdrop = false;
        }
    }

    /// <summary>快照生成期间的线程级重入闸（父级 owner-draw 不得再取快照）。</summary>
    [ThreadStatic]
    private static bool _capturingBackdrop;

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

    private static string CacheKey(ResolvedMaterial material, Rectangle bounds, int dpi, SurfaceLightState? light)
        => $"{MaterialKey(material)}:{bounds.Width}x{bounds.Height}@dpi{dpi}"
           + (light is { } state ? "|light:" + state.CacheToken : string.Empty);

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
        internal bool SuppressPaint { get; set; }
        internal System.Windows.Forms.Timer? RefreshTimer { get; set; }
    }
}
