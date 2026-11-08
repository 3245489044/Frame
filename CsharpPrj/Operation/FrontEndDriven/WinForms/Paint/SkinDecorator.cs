#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.WinForms.Applier;
using Modules.CapabilityResolution;
using Share.Conversation.Rendering.Surface;
using Share.SurfacePaint;
using Share.SurfacePaint.Hosting;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 皮肤装饰器（**WinForms 薄绑定层**，owner-draw 接线）：按 <see cref="ControlDescriptor.SkinRefs"/> 取层栈绘制。
/// <para>绘制编排/缓存/阶段/报告 = <see cref="SurfacePaintSession"/>（Share.SurfacePaint，载体无关）；
/// 本类只做：材质选择、Paint 事件挂载、视口/快照/光端口装配、定时失效与诊断记账。</para>
/// <para>默认关闭（<see cref="Enabled"/>=false）：未接线时回到纯属性着色，零行为变化。</para>
/// </summary>
public static class SkinDecorator
{
    /// <summary>是否启用 owner-draw（回退开关；默认关闭）。</summary>
    public static bool Enabled { get; set; }

    /// <summary>
    /// 窗口帧视图声明端口（装配期由宿主注入一次；null ⇒ 渲染器走显式回退并记
    /// <c>surface-frame-view-unavailable</c>）。
    /// </summary>
    public static ISurfaceFrameGatherSource? FrameGatherSource { get; set; }

    private static readonly ConditionalWeakTable<Control, SkinState> States = new();

    /// <summary>属性通道已由材质投影器接管的控件（单一写者判据：声明属性不得再写 BackColor）。</summary>
    private static readonly ConditionalWeakTable<Control, object> PropertyOwned = new();

    private static readonly object PropertyOwnedMarker = new();

    /// <summary>按描述符挂载/更新绘制（幂等；未启用/无材质/不支持 ⇒ 卸除，禁残留旧绘制）。</summary>
    public static void Attach(Control? control, ControlDescriptor descriptor)
    {
        if (control is null) return;
        if (!Enabled)
        {
            Detach(control);
            return;
        }

        // 族声明的材质槽优先：结构宿主（host_surface）不得被背景槽（panel_surface→panel_frost）顶替，
        // 否则 SplitContainer 自身与两个 SplitterPanel 会把整窗铺成同一块玻璃面（panelToPage → 1.0）。
        var family = WinFormsControlFamilyMap.Resolve(control, descriptor.Type);
        var familyDraw = SkinMaterialCatalog.FamilyDraw(family?.MaterialSlot);
        var material = familyDraw
            ?? (descriptor.SkinRefs is { Count: > 0 }
                ? SelectMaterial(descriptor.SkinRefs)
                : SelectMaterial(SkinMaterialCatalog.For(descriptor.Id) ?? SkinMaterialCatalog.For(descriptor.Name))
                  ?? SkinMaterialCatalog.Global);
        // 属性通道族不依赖绘制通道材质：缺绘制材质时仍要走属性面，否则会被 WinFormApplier 的样式桥
        // 剥掉旧底色后无任何写入，退化成系统色（property_button 白岛）。
        if (material is null && family is not { Strategy: SurfaceStrategy.PropertyFace })
        {
            Detach(control);
            return;
        }

        // 白名单外控件不能 owner-draw，但保持系统浅色会破坏整体玻璃观感 ⇒ 只做「属性级换肤」。
        if (!SupportsOwnerDraw(control))
        {
            int? tint = null;
            ResolvedMaterial? propertyMaterial;
            if (family is { Strategy: SurfaceStrategy.PropertyFace })
            {
                // B 类：族级 control_face（glass_fill）优先；不再因 FallbackUsed 换 GlobalProperty(row_surface)。
                propertyMaterial = SkinMaterialCatalog.FamilyProperty(family.MaterialSlot)
                                   ?? SelectPropertyMaterial(descriptor);
                tint = SurfaceStyleBridge.TintFor(descriptor, family.StyleRole);
                if (propertyMaterial is { FallbackStep: > SurfaceFallbackStep.L2SolidFace })
                {
                    FrontEndDrivenLog.Log("SkinDecorator_face_degraded", new
                    {
                        type = control.GetType().Name,
                        family = family.TypeKey,
                        material = propertyMaterial.MaterialId,
                        step = propertyMaterial.FallbackStep.ToString(),
                    });
                }
            }
            else
            {
                // 未登记族保持旧路径（零回归）：降级才换全局实心材质。
                propertyMaterial = SelectPropertyMaterial(descriptor);
                if (propertyMaterial is { FallbackUsed.Length: > 0 } && SkinMaterialCatalog.GlobalProperty is { } propertyFallback)
                {
                    propertyMaterial = propertyFallback;
                }
            }

            var themed = propertyMaterial is not null && ApplyFallbackTheme(control, propertyMaterial, tint);
            Detach(control);
            if (!themed)
            {
                FrontEndDrivenLog.Log("SkinDecorator_unsupported", new
                {
                    type = control.GetType().Name,
                    material = propertyMaterial?.MaterialId,
                    channel = WinFormsPropertyProjector.ChannelId,
                });
            }

            return;
        }

        // 空性收窄：PropertyFace 族已在属性分支返回，其余族缺材质已在上方首个判据 Detach（此处不可达）。
        if (material is null)
        {
            Detach(control);
            return;
        }

        var state = States.GetValue(control, static _ => new SkinState());
        // 结构宿主改用 host_surface(flat) 后自身材质不再带 stroke；族声明 FrameHost 的控件由边框宿主补 1px 内衬边。
        state.EdgeMode = family?.EdgeMode ?? SurfaceEdgeMode.Material;
        if (state.EdgeMode == SurfaceEdgeMode.FrameHost && SurfaceFrameHost.EdgeStrokeFor(material) is null)
        {
            FrontEndDrivenLog.Log("SkinDecorator_frame_host_unavailable", new
            {
                type = control.GetType().Name,
                family = family?.TypeKey,
                material = material.MaterialId,
                reason = "stroke-source-missing",
            });
        }

        var key = SurfacePaintSession.FingerprintOf(material);
        if (string.Equals(state.MaterialKey, key, StringComparison.Ordinal) && state.Handler is not null)
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

        state.MaterialKey = key;
        if (state.Session is null)
        {
            var host = new WinFormsSurfaceHost(control);
            var backdrop = new WinFormsBackdropSource(control);
            var light = new WinFormsLightSource(control);
            state.Session = new SurfacePaintSession(
                SurfaceAmbient.IdOf(control), host, backdrop, light,
                WinFormsSurfacePorts.Engine ?? WinFormsSurfaceEnginePort.Instance,
                WinFormsSurfacePorts.Log);
            // 首挂：双缓冲失败只记账不抛（绘制仍正确，仅闪烁风险，禁静默）。
            host.PrepareSurface();
        }

        state.Session.Attach(material, state.EdgeMode);

        if (state.Handler is null)
        {
            var target = control;
            state.Handler = (_, e) => PaintControl(target, state, e);
            control.Paint += state.Handler;
            control.Disposed += (_, _) => Detach(target);
            SkinDiagnostics.RecordAttach(true);
        }

        // 窗口级刷新：一次 tick 生产一帧；控件不再各建 Timer。
        var form = control.FindForm();
        SurfaceRefreshHost.Ensure(form);
        SurfaceRefreshHost.MarkDirty(control, "theme");

        control.Invalidate();
    }


    /// <summary>关闭 owner-draw 并卸除全部已挂绘制（开关关闭 ⇒ 回到纯属性着色，零残留）。</summary>
    public static void Disable()
    {
        Enabled = false;
        SurfaceAmbient.Reset();
        // 关肤后属性通道归还声明侧：清空接管标记，否则声明颜色会被永久剥离（E2 豁免失效）。
        PropertyOwned.Clear();
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
        state.Session?.Dispose();
        state.Session = null;
        state.MaterialKey = null;
        SurfaceAmbient.Release(control);
        States.Remove(control);
        SkinDiagnostics.RecordAttach(false);
    }

    /// <summary>控件当前生效材质（未挂载 ⇒ null；断言/诊断读取面）。</summary>
    /// <param name="control">目标控件。</param>
    public static ResolvedMaterial? MaterialOf(Control? control)
        => control is not null && States.TryGetValue(control, out var state) ? state.Session?.Material : null;

    /// <summary>
    /// 按窗口帧卡片键失效（仅命中本窗口已挂载控件；供 <see cref="SurfaceRefreshHost"/> 的逐卡片失效使用）。
    /// </summary>
    internal static void InvalidateCards(Form? form, IReadOnlyCollection<string> keys)
    {
        if (form is null || keys.Count == 0) return;

        var wanted = new HashSet<string>(keys, StringComparer.Ordinal);
        foreach (var pair in States)
        {
            var control = pair.Key;
            if (control.IsDisposed || !ReferenceEquals(control.FindForm(), form)) continue;
            if (!wanted.Contains(SurfaceAmbient.IdOf(control))) continue;
            control.Invalidate();
        }
    }

    /// <summary>
    /// 采集窗口级帧输入（**UI 线程**）：控件/材质/视口 → 不可变请求集；组合器随后可在后台执行。
    /// <para>不再逐控件抓背景：背景由窗口级事实/后续批次统一提供。</para>
    /// </summary>
    internal static SurfaceFrameInput? CaptureWindow(Form form)
    {
        if (form is null || form.IsDisposed) return null;

        var windowViewport = SurfaceAmbient.ViewportOf(form);
        if (!windowViewport.IsValid) return null;

        var cards = new List<SurfaceCardRequest>();
        foreach (var pair in States)
        {
            var control = pair.Key;
            var state = pair.Value;
            if (control.IsDisposed || !control.IsHandleCreated || !control.Visible) continue;
            if (!ReferenceEquals(control.FindForm(), form)) continue;

            var material = state.Session?.Material;
            if (material is null) continue;

            var viewport = SurfaceAmbient.ViewportOf(control);
            if (!viewport.IsValid) continue;

            cards.Add(new SurfaceCardRequest(SurfaceAmbient.IdOf(control), viewport, material, Backdrop: null));
        }

        if (cards.Count == 0) return null;

        // 窗口级背景：唯一 producer（2s 节流 + 重入闸）；失败为显式 Unavailable，不伪造像素。
        var backdrop = SurfaceWindowBackdropSource.Capture(form);
        SurfaceHostFacts.DeclareBackdrop(backdrop);

        return new SurfaceFrameInput(
            SurfaceAmbient.IdOf(form),
            windowViewport,
            ResolveWindowLight(windowViewport),
            cards,
            Array.Empty<string>(),
            backdrop);
    }

    private static SurfaceLightState? ResolveWindowLight(SurfaceViewport viewport)
    {
        var frame = SurfaceLightRegistry.Current;
        if (frame is null) return null;

        var coverage = new SurfaceLightCoverage(
            (float)viewport.HostOriginXDip,
            (float)viewport.HostOriginYDip,
            viewport.HostWidth,
            viewport.HostHeight);
        var probe = new SurfaceLightProbe(
            coverage.X, coverage.Y, coverage.Width, coverage.Height, ZOrder: 0, Dpi: viewport.HostDpi);
        return SurfaceLightState.From(SurfaceLightPort.Sample(frame, probe));
    }

    /// <summary>是否属于 owner-draw 白名单（断言前置：白名单外永远不着色）。</summary>
    /// <param name="control">目标控件。</param>
    public static bool IsOwnerDrawSupported(Control? control)
        => control is not null && SupportsOwnerDraw(control);

    /// <summary>是否支持 owner-draw（容器/文本承载类；其余控件自绘内容）。</summary>
    private static bool SupportsOwnerDraw(Control control)
    {
        var family = WinFormsControlFamilyMap.Resolve(control, null);
        if (family is not null)
        {
            return family.Strategy is SurfaceStrategy.OwnerDrawContainer or SurfaceStrategy.InheritParent;
        }

        // 未登记 ⇒ 旧白名单（零回归）。
        return control is Panel or FlowLayoutPanel or TableLayoutPanel or UserControl or GroupBox or Label;
    }

    /// <summary>该控件的属性通道（BackColor）是否已由材质投影器接管（唯一写者判据）。</summary>
    /// <param name="control">目标控件。</param>
    public static bool IsPropertyChannelOwned(Control? control)
        => control is not null && PropertyOwned.TryGetValue(control, out _);

    /// <summary>
    /// 非 owner-draw 控件的属性级换肤：**委托属性通道投影器**（唯一写点；不透明化与幂等比对都在投影器内）。
    /// </summary>
    private static bool ApplyFallbackTheme(Control control, ResolvedMaterial material, int? tintArgb = null)
    {
        if (!WinFormsPropertyProjector.TryApply(control, material, tintArgb, out _)) return false;
        PropertyOwned.Remove(control);
        PropertyOwned.Add(control, PropertyOwnedMarker);
        return true;
    }

    /// <summary>一帧绘制：委托 <see cref="SurfacePaintSession"/>；本层只做重入闸、异常隔离与诊断记账。</summary>
    private static void PaintControl(Control control, SkinState state, PaintEventArgs e)
    {
        // 窗口级背板采集期间会被父级 WM_PRINT 重入；此时留白（父级背景即「背后」），防递归。
        if (SurfaceWindowBackdropSource.IsCapturing) return;

        var session = state.Session;
        if (session is null) return;

        // 失败后不再走绘制通道（避免同帧反复抛）；保持属性实心面。
        if (state.Failed)
        {
            ApplyDegradedFace(control, state);
            return;
        }

        try
        {
            var bounds = new Rectangle(Point.Empty, control.ClientSize);
            var snapshot = SurfaceRefreshHost.SnapshotOf(control.FindForm());
            var result = session.Paint(e.Graphics, bounds, snapshot);
            var report = result.Report;

            SurfaceRefreshHost.ReleaseConsumed(control, snapshot);

            SkinDiagnostics.RecordFrame(report);
            if (session.Material is { } material)
            {
                SkinDiagnostics.RecordPaintCount(material.MaterialId, report);
            }

            if (report.Details.Count > 0)
            {
                FrontEndDrivenLog.Log("SkinDecorator_skipped", new
                {
                    material = session.Material?.MaterialId,
                    skipped = string.Join(";", report.Details),
                });
            }
        }
        catch (Exception ex)
        {
            // P2-2：绘制链异常不得穿透消息泵；记账后降级到属性实心面。
            state.Failed = true;
            FrontEndDrivenLog.Log("SkinDecorator_paint_failed", new
            {
                type = control.GetType().Name,
                control = control.Name,
                material = session.Material?.MaterialId,
                exception = ex.ToString(),
            });
            ApplyDegradedFace(control, state);
        }
    }

    /// <summary>绘制失败后的属性通道降级（唯一写点仍走投影器；失败只记账）。</summary>
    private static void ApplyDegradedFace(Control control, SkinState state)
    {
        var material = state.Session?.Material;
        if (material is null) return;

        try
        {
            ApplyFallbackTheme(control, material);
        }
        catch (Exception ex)
        {
            FrontEndDrivenLog.Log("SkinDecorator_paint_failed", new
            {
                type = control.GetType().Name,
                control = control.Name,
                material = material.MaterialId,
                exception = ex.ToString(),
            });
        }
    }

    /// <summary>冻结一帧输入（探针：绘制期间不再自行解析时间/光标/快照）。</summary>
    internal static IDisposable PinInputs(Control control, SurfaceRenderInputs inputs)
        => States.TryGetValue(control, out var state) && state.Session is { } session
            ? session.PinInputs(inputs)
            : NoopScope.Instance;

    /// <summary>构建一帧动态输入（运行时光 + 背后快照）；探针用它冻结「实际/期望」同一份输入。</summary>
    internal static SurfaceRenderInputs BuildRenderInputs(
        Control control, ResolvedMaterial material, SurfaceLightState? light = null)
        => States.TryGetValue(control, out var state) && state.Session is { } session
            ? session.BuildRenderInputs(SurfaceAmbient.ViewportOf(control), light)
            : SurfaceRenderInputs.Create(light, null, 0);

    /// <summary>最近一帧证据（探针据此区分「没画」与「画了但颜色错」）。</summary>
    internal static SkinFrameReport LastFrameOf(Control? control)
        => control is not null && States.TryGetValue(control, out var state) && state.Session is { } session
            ? new SkinFrameReport(session.Frame, session.LastReport)
            : new SkinFrameReport(0, SurfacePaintReport.Empty);

    /// <summary>帧证据（帧序号 + 结构化报告）。</summary>
    internal readonly record struct SkinFrameReport(long Frame, SurfacePaintReport Report);

    /// <summary>测试/诊断用可控光源（转发载体光源端口）。</summary>
    internal static Func<Control, SurfaceLightState?>? LightOverride
    {
        get => WinFormsLightSource.Override;
        set => WinFormsLightSource.Override = value;
    }

    private sealed class NoopScope : IDisposable
    {
        internal static readonly NoopScope Instance = new();

        public void Dispose() { }
    }

    /// <summary>槽优先选择（唯一实现：<see cref="SurfaceSlotSelection"/>）。</summary>
    private static ResolvedMaterial? SelectMaterial(IReadOnlyDictionary<string, ResolvedMaterial>? materials)
        => SurfaceSlotSelection.SelectBackground(materials);

    /// <summary>属性通道材质：声明 Id/Name 槽 → 全局属性材质（属性面与旧路径共用同一梯度）。</summary>
    private static ResolvedMaterial? SelectPropertyMaterial(ControlDescriptor descriptor)
        => SelectMaterial(
               SkinMaterialCatalog.ForProperty(descriptor.Id)
               ?? SkinMaterialCatalog.ForProperty(descriptor.Name))
           ?? SkinMaterialCatalog.GlobalProperty;

    private sealed class SkinState
    {
        /// <summary>材质指纹（同材质不重挂）。</summary>
        internal string? MaterialKey { get; set; }

        /// <summary>族声明的边缘归类（Material = 材质自带 stroke；FrameHost = 由边框宿主补边）。</summary>
        internal SurfaceEdgeMode EdgeMode { get; set; } = SurfaceEdgeMode.Material;

        internal PaintEventHandler? Handler { get; set; }

        /// <summary>通用绘制会话（缓存/阶段/报告）。</summary>
        internal SurfacePaintSession? Session { get; set; }

        /// <summary>绘制链已失败（后续帧走属性实心面，不再重复抛）。</summary>
        internal bool Failed { get; set; }
    }
}

