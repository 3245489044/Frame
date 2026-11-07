#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.WinForms.Applier;
using Modules.CapabilityResolution;
using Share.Conversation.Rendering.Surface;
using UiBackend;

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

    /// <summary>属性通道已由材质投影器接管的控件（E2 单一写者判据：声明属性不得再写 BackColor）。</summary>
    private static readonly ConditionalWeakTable<Control, object> PropertyOwned = new();

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
        var family = SurfaceControlFamilyRegistry.ForControl(control, descriptor.Type);
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

        // 白名单外控件（Button/TextBox/ListBox/TreeView/TabControl…）不能 owner-draw，
        // 但保持系统浅色会直接破坏整体玻璃观感 ⇒ 只做「属性级换肤」（底色/文字色，可逆重启）。
        if (!SupportsOwnerDraw(control))
        {
            int? tint = null;
            ResolvedMaterial? propertyMaterial;
            if (family is { Strategy: SurfaceStrategy.PropertyFace })
            {
                // B 类：族级 control_face（glass_fill）优先；不再因 FallbackUsed 换 GlobalProperty(row_surface)。
                propertyMaterial = SkinMaterialCatalog.FamilyProperty(family.MaterialSlot)
                                   ?? SelectMaterial(
                                       SkinMaterialCatalog.ForProperty(descriptor.Id)
                                       ?? SkinMaterialCatalog.ForProperty(descriptor.Name))
                                   ?? SkinMaterialCatalog.GlobalProperty;
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
                // 未登记族保持旧路径（零回归）：属性通道用已按通道能力降级的材质，降级才换全局实心材质。
                propertyMaterial = SelectMaterial(
                                       SkinMaterialCatalog.ForProperty(descriptor.Id)
                                       ?? SkinMaterialCatalog.ForProperty(descriptor.Name))
                                   ?? SkinMaterialCatalog.GlobalProperty;
                if (propertyMaterial is { FallbackUsed.Length: > 0 } && SkinMaterialCatalog.GlobalProperty is { } propertyFallback)
                {
                    propertyMaterial = propertyFallback;
                }
            }

            var themed = propertyMaterial is not null && ApplyFallbackTheme(control, propertyMaterial, tint);
            Detach(control);
            // 只有「连属性级换肤也做不到」才记账（可承载的控件静默换色，避免每控件一条噪声）。
            if (!themed)
            {
                FrontEndDrivenLog.Log("SkinDecorator_unsupported", new
                {
                    type = control.GetType().Name,
                    // 通道错标防护：属性分支只记属性材质（缺失即 null），禁回退成 draw 材质名。
                    material = propertyMaterial?.MaterialId,
                    channel = WinFormsPropertyProjector.ChannelId,
                });
            }

            return;
        }

        if (material is null)
        {
            Detach(control);
            return;
        }

        var state = States.GetValue(control, static _ => new SkinState());
        // 批次 1.5：结构宿主改用 host_surface(flat) 后自身材质不再带 stroke；
        // 族声明 EdgeMode=FrameHost 的控件由边框宿主补 1px 内衬边（borderToPanel 归零即此因）。
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

        EnsureRefreshTimer(control, state, NeedsLight(material));

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
        state.RefreshTimer?.Stop();
        state.RefreshTimer?.Dispose();
        state.RefreshTimer = null;
        state.Cache?.Dispose();
        state.Cache = null;
        state.CacheKey = null;
        state.BackdropVersion = 0;
        state.CacheBackdropVersion = 0;
        state.LastBackdropTick = 0;
        state.Material = null;
        state.MaterialKey = null;
        state.Pinned = null;
        SurfaceAmbient.Release(control);
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
    {
        var family = SurfaceControlFamilyRegistry.ForControl(control, null);
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

    private static readonly object PropertyOwnedMarker = new();

    private static void PaintControl(Control control, SkinState state, PaintEventArgs e)
    {
        // 生成 backdrop 快照时，本控件会被父级 DrawToBitmap 重入；此时留白（父级背景即「背后」），防递归。
        if (state.SuppressPaint) return;

        var material = state.Material;
        if (material is null) return;

        var bounds = new Rectangle(Point.Empty, control.ClientSize);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var paintBegin = SkinRenderProfile.Begin();
        var baseReport = state.BaseReport;
        try
        {
            // 静态/动态拆分：backdrop+fill+edge 只随「分钟光 + 背后快照」变化，缓存成底图；
            // 只有 specular 随秒级时间/光标变化，逐帧重画（一次渐变填充，成本恒定）。
            var pinned = state.Pinned;
            var baseLight = pinned is { } frozen
                ? frozen.Light
                : NeedsLight(material) ? ResolveBaseLight(control) : (SurfaceLightState?)null;
            var now = Environment.TickCount64;
            var backdropDue = pinned is null && HasBackdrop(material)
                && (state.Cache is null || now - state.LastBackdropTick >= BackdropRefreshMs);
            var backdropVersion = pinned?.BackdropVersion ?? state.BackdropVersion;
            // K4：指纹在 Attach 期已算好；此处只读缓存值（缺失才补算一次），禁每帧重算摘要。
            var fingerprint = FingerprintOf(state, material);
            var key = CacheKey(fingerprint, bounds, control.DeviceDpi, baseLight, backdropVersion);
            var hit = state.Cache is not null
                      && !backdropDue
                      && string.Equals(state.CacheKey, key, StringComparison.Ordinal);
            // 结构宿主（FrameHost）的背景由页面装饰层统一接管：用同一张页面场位图，不再铺纯色 flat。
            var ambientHost = state.EdgeMode == SurfaceEdgeMode.FrameHost && SurfaceAmbient.Enabled;
            if (!hit && !ambientHost) baseReport = RebuildBase(control, material, bounds, state, pinned, baseLight);
            // 页面装饰层接管背景（ambientHost）或命中缓存但首帧尚无 BaseReport 时，baseReport 可为 null；
            // 此处归一为空报告，保证 K6 合并面始终有值（不改 SurfaceAmbient 语义）。
            if (baseReport is null) baseReport = SkinPaintReport.Empty;

            if (ambientHost) SurfaceAmbient.PaintHostSurface(control, e.Graphics);
            else if (state.Cache is not null) e.Graphics.DrawImageUnscaled(state.Cache, bounds);

            var light = pinned is { } frozenLight
                ? frozenLight.Light
                : NeedsLight(material) ? ResolveLight(control) : (SurfaceLightState?)null;
            // K6：静态底图 + 动态层合并成一份帧报告；PaintCount 在这里（每帧一次）推进。
            var dynamicReport = SkinPaintEngine.PaintReport(
                e.Graphics, bounds, material, null, light, PaintScope.Dynamic);
            var frameReport = baseReport.Merge(dynamicReport);
            // 边框宿主：内容之上补 1px 内衬边（host_surface=flat 本身没有 stroke，必须由宿主补）。
            if (state.EdgeMode == SurfaceEdgeMode.FrameHost)
            {
                SurfaceFrameHost.TryDraw(e.Graphics, bounds, state.EdgeMode, material, control.DeviceDpi, out _);
            }

            // 卡片唯一装饰消费入口：同一份页面场（留白环/外发光/双形态边缘光/纹理/丝带），无卡片分支。
            if (SurfaceAmbient.ShouldChrome(material))
            {
                SurfaceAmbient.PaintPanelChrome(control, e.Graphics, bounds, material);
            }

            state.Frame++;
            state.LastReport = frameReport;
            SkinDiagnostics.RecordFrame(frameReport);
            if (frameReport.Details.Count > 0)
            {
                FrontEndDrivenLog.Log("SkinDecorator_skipped", new { material = material.MaterialId, skipped = string.Join(";", frameReport.Details) });
            }

            SkinRenderProfile.RecordPaint(paintBegin, hit);
        }
        catch (Exception ex)
        {
            // 绘制失败必须可见（带堆栈），且不得把异常抛回 UI 线程中断整棵重绘；
            // K6：整帧失败同样推进 Frame 并留下 Failed 报告（不让「没有报告」冒充成功）。
            state.Frame++;
            state.LastReport = new SkinPaintReport(1, 0, 0, 0, 1, new[] { "frame:" + ex.GetType().Name + ":" + ex.Message });
            SkinDiagnostics.RecordFrame(state.LastReport);
            SkinRenderProfile.RecordFailure();
            FrontEndDrivenLog.Log("SkinDecorator_paint_failed", new
            {
                type = control.GetType().Name,
                material = material.MaterialId,
                exception = ex.ToString(),
            });
        }
    }

    /// <summary>重建静态底图：backdrop（快照+模糊）+ fill + edge；specular 不进入底图。</summary>
    private static SkinPaintReport RebuildBase(
        Control control, ResolvedMaterial material, Rectangle bounds, SkinState state,
        SkinRenderInputs? pinned, SurfaceLightState? baseLight)
    {
        var renderBegin = SkinRenderProfile.Begin();
        SkinRenderInputs? owned = null;
        var inputs = pinned;
        if (inputs is null && NeedsLight(material))
        {
            owned = BuildRenderInputs(control, material, baseLight);
            inputs = owned;
            // 快照失败的帧保留上一张底图（否则会把 emulated 近似永久缓存，出现整栏变黑/变灰）。
            if (HasBackdrop(material) && inputs.Backdrop is null && state.Cache is not null)
            {
                owned.Dispose();
                state.LastBackdropTick = Environment.TickCount64;
                // 快照缺失 ⇒ 保留上一张底图；本帧沿用上一次静态报告（不让「未重建」冒充 Skipped）。
                return state.BaseReport ?? SkinPaintReport.Empty;
            }
        }

        var skipped = SkinPaintReport.Empty;
        var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            skipped = SkinPaintEngine.PaintReport(
                graphics, bounds, material, inputs?.Backdrop, inputs?.Light, PaintScope.Static);
            if (skipped.Details.Count > 0)
            {
                FrontEndDrivenLog.Log("SkinDecorator_skipped", new { material = material.MaterialId, skipped = string.Join(";", skipped.Details) });
            }
        }

        state.Cache?.Dispose();
        state.Cache = bitmap;
        state.CacheBackdropVersion = inputs?.BackdropVersion ?? 0;
        if (pinned is null && inputs is not null) state.BackdropVersion = inputs.BackdropVersion;
        state.CacheKey = CacheKey(FingerprintOf(state, material), bounds, control.DeviceDpi,
            baseLight, state.CacheBackdropVersion);
        state.LastBackdropTick = Environment.TickCount64;
        state.BaseReport = skipped;
        owned?.Dispose();
        SkinRenderProfile.RecordRender(renderBegin);
        return skipped;
    }

    /// <summary>底图用的「静态光」：同一份全局采样，缓存键已量化到肉眼可辨粒度（见 <see cref="SurfaceLightState.CacheToken"/>）。</summary>
    private static SurfaceLightState? ResolveBaseLight(Control control) => ResolveLight(control);

    /// <summary>背后快照的刷新周期：背后内容变化是低频事件，2s 足够，避免每帧全父级 DrawToBitmap。</summary>
    private const int BackdropRefreshMs = 2000;

    /// <summary>
    /// 构建一帧的动态输入（运行时光 + 背后快照）。在绘制循环外调用时可安全取父级快照；
    /// 探针用它冻结「实际 / 期望」同一份输入（<see cref="PinInputs"/>）。
    /// </summary>
    internal static SkinRenderInputs BuildRenderInputs(
        Control control, ResolvedMaterial material, SurfaceLightState? light = null)
    {
        var state = States.GetValue(control, static _ => new SkinState());
        SurfaceLightState? resolvedLight = NeedsLight(material) ? light ?? ResolveLight(control) : null;
        if (!HasBackdrop(material)) return SkinRenderInputs.Create(resolvedLight, null, 0);

        var bounds = new Rectangle(Point.Empty, control.ClientSize);
        var snapshotBegin = SkinRenderProfile.Begin();
        var backdrop = TryCaptureBehind(control, bounds, state);
        SkinRenderProfile.RecordSnapshot(snapshotBegin);
        var version = backdrop is null ? 0 : Interlocked.Increment(ref _backdropSerial);
        return SkinRenderInputs.Create(resolvedLight, backdrop, version);
    }

    /// <summary>冻结一帧输入：绘制期间 <see cref="PaintControl"/> 不再自行解析时间/光标/快照，保证可复现。</summary>
    internal static IDisposable PinInputs(Control control, SkinRenderInputs inputs)
    {
        if (!States.TryGetValue(control, out var state)) return NoopScope.Instance;
        var previous = state.Pinned;
        state.Pinned = inputs;
        return new PinScope(state, previous);
    }

    /// <summary>
    /// 最近一帧证据（探针据此区分「没画」与「画了但颜色错」，不靠像素猜测）。
    /// <para>K6：帧序号与绘制结果同源；<c>Painted</c> = 该帧至少一层 Ok，而不是「处理器被调用」。</para>
    /// </summary>
    internal static SkinFrameReport LastFrameOf(Control? control)
        => control is not null && States.TryGetValue(control, out var state)
            ? new SkinFrameReport(state.Frame, state.LastReport ?? SkinPaintReport.Empty)
            : new SkinFrameReport(0, SkinPaintReport.Empty);

    /// <summary>帧证据（帧序号 + 结构化报告）。</summary>
    internal readonly record struct SkinFrameReport(long Frame, SkinPaintReport Report);

    private static int _backdropSerial;

    private sealed class PinScope : IDisposable
    {
        private SkinState? _state;
        private readonly SkinRenderInputs? _previous;

        internal PinScope(SkinState state, SkinRenderInputs? previous)
        {
            _state = state;
            _previous = previous;
        }

        public void Dispose()
        {
            if (_state is null) return;
            _state.Pinned = _previous;
            _state = null;
        }
    }

    private sealed class NoopScope : IDisposable
    {
        internal static readonly NoopScope Instance = new();
        public void Dispose() { }
    }

    /// <summary>
    /// 运行时光：只从全局光场 <see cref="SurfaceLightRegistry.Current"/> 采样；没有帧 ⇒ null（载体保持声明值）。
    /// <para>不再读取时间/光标；组件的位置与尺寸是唯一输入。</para>
    /// </summary>
    private static SurfaceLightState? ResolveLight(Control control)
    {
        if (LightOverride is { } over) return over(control);

        SurfaceLightPump.EnsureStarted(control);
        // 无帧（启动前 1 秒 / 引擎未加载 / 连续采样失败）⇒ 声明式默认太阳，而不是整屏无光。
        var frame = SurfaceLightRegistry.Current ?? SurfaceLight.DefaultFrame(SurfaceLightPump.ScreenCoverage(control));
        var probe = ProbeOf(control);
        var sample = SurfaceLightPort.Sample(frame, probe);
        if (sample.HasValue && !sample.SpotInsideBounds)
        {
            // 越界只记账、不夹取：光斑允许落在面板之外，是否受光由 Shade 判定。
            SkinDiagnostics.RecordCarrierOutOfBounds(
                "winforms:" + (string.IsNullOrEmpty(control.Name) ? control.GetType().Name : control.Name),
                sample.SpotBoundsDetail);
        }

        return sample.HasValue ? SurfaceLightState.From(sample) : null;
    }

    /// <summary>控件矩形 → 屏幕 DIP 探针（origin 只能来自 <see cref="SurfaceLightCarrierOrigin"/> 单一入口）。</summary>
    private static SurfaceLightProbe ProbeOf(Control control)
    {
        var carrier = SurfaceLightCarrierOrigin.Of(control);
        return new SurfaceLightProbe(
            carrier.ScreenXDip,
            carrier.ScreenYDip,
            carrier.WidthDip,
            carrier.HeightDip,
            ZOrder: 0,
            Dpi: carrier.Dpi);
    }

    /// <summary>
    /// 测试/诊断用可控光源（null ⇒ 按时间+光标解析）。只替换动态光解析，不改变静态底光的分钟量化，
    /// 以便在「同一张静态底图」上复现光标 ±12° 的动态变化。
    /// </summary>
    internal static Func<Control, SurfaceLightState?>? LightOverride { get; set; }

    private static bool HasBackdrop(ResolvedMaterial material)
        => material.Layers.Any(layer => layer.Primitive == "backdrop");

    /// <summary>是否需要运行时光：backdrop 与 specular 都依赖它；只按 backdrop 判定会让金属/玻璃高光退化成固定暗带。</summary>
    private static bool NeedsLight(ResolvedMaterial material)
        => material.Layers.Any(layer =>
            layer.Primitive == "backdrop" || layer.Primitive == "specular");

    /// <summary>玻璃材质每秒重画动态 sheen；静态底图只在分钟光/背后快照（2s）变化时重建。</summary>
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

    /// <summary>WM_PRINT：把窗口自身画到指定 HDC（不带 PRF_CHILDREN 即不含任何子控件）。</summary>
    private const int WmPrint = 0x0317;

    /// <summary>PRF_CLIENT：绘制客户区。</summary>
    private const int PrfClient = 0x00000004;

    /// <summary>PRF_NONCLIENT：绘制非客户区（边框/滚动条）。</summary>
    private const int PrfNonClient = 0x00000002;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// 面板背后的快照来源（不新增合成抽象）：父级自身表面 + 非本子树的兄弟内容。
    /// <para>
    /// 关键：**排除本控件的整棵子树**。子树里的子控件默认底（<see cref="SystemColors.Control"/>）与
    /// WebView2/自身文字一旦被整块 <c>DrawToBitmap</c> 拍进背板，就会被玻璃洗白成 <c>#5B7386</c> 并留下鬼影字。
    /// </para>
    /// <para>边界：不穿越窗口/WebView2 HWND ⇒ 不做元素级看穿桌面（与 01 文档可达上限一致）。</para>
    /// </summary>
    private static Bitmap? TryCaptureBehind(Control control, Rectangle bounds, SkinState state)
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
        Bitmap? crop = null;
        _capturingBackdrop = true;
        state.SuppressPaint = true;
        var page = SkinMaterialCatalog.PageBackground;
        var parentBackColor = parent.BackColor;
        // 父级背景仍是系统默认（未上色）⇒ 背后就是页面，快照必须以页面色起底，否则面板被浅灰洗白。
        var unstyled = page.HasValue
                      && (parentBackColor.IsEmpty
                          || parentBackColor.ToArgb() == SystemColors.Control.ToArgb());
        try
        {
            parentBitmap = new Bitmap(parentSize.Width, parentSize.Height);
            if (page.HasValue)
            {
                using var prefill = Graphics.FromImage(parentBitmap);
                prefill.Clear(page.Value);
            }

            // 父级自身已是玻璃（含 backdrop 层）时，再拍进来就是「玻璃叠玻璃」：同一 fill 二次合成，
            // 面色被抬到 #29475F、背后渐变被抹平（panel/page 由 1.66 抬到 1.95）。此时只保留页面色起底，
            // 玻璃合成只做一层；父级非玻璃（实心/内容面）才需要拍自身表面。
            var parentIsGlass = MaterialOf(parent) is { } parentMaterial
                                && parentMaterial.Layers.Any(layer => layer.Primitive == "backdrop");
            if (unstyled) parent.BackColor = page!.Value;
            try
            {
                // 只打父级自身表面（不带 PRF_CHILDREN）：子控件（尤其 WebView2 与未换肤面板的默认底）
                // 一旦被拍进背板，就会被玻璃洗白成 #5B7386 并留下鬼影字。
                if (!parentIsGlass || !page.HasValue)
                {
                    PaintParentSurface(parent, parentBitmap);
                }
            }
            finally
            {
                if (unstyled) parent.BackColor = parentBackColor;
            }

            // 兄弟内容仍属于「背后」：逐个按交集合成，跳过本控件所在祖先链（自身子树）。
            CompositeSiblings(parent, SubtreeRootIn(parent, control), source, parentBitmap);

            crop = new Bitmap(bounds.Width, bounds.Height);
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
            crop?.Dispose();
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

    /// <summary>父级自身表面绘制（不含任何子控件）；子控件由 <see cref="CompositeSiblings"/> 单独合成。</summary>
    private static void PaintParentSurface(Control parent, Bitmap bitmap)
    {
        using var graphics = Graphics.FromImage(bitmap);
        var hdc = graphics.GetHdc();
        try
        {
            SendMessage(parent.Handle, WmPrint, hdc, (IntPtr)(PrfClient | PrfNonClient));
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }
    }

    /// <summary>本控件在父级中的直接子节点（祖先链根）：快照必须排除它整棵子树。</summary>
    private static Control? SubtreeRootIn(Control parent, Control control)
    {
        var node = control;
        while (node is not null && !ReferenceEquals(node.Parent, parent)) node = node.Parent;
        return node;
    }

    /// <summary>
    /// 合成父级的兄弟内容（跳过 <paramref name="excluded"/> 整棵子树）：只取与 <paramref name="source"/>
    /// 相交的部分，按 z 序后→前绘制。单个兄弟失败只记账，不中断整张背板。
    /// </summary>
    private static void CompositeSiblings(Control parent, Control? excluded, Rectangle source, Bitmap bitmap)
    {
        using var graphics = Graphics.FromImage(bitmap);
        for (var i = parent.Controls.Count - 1; i >= 0; i--)
        {
            var sibling = parent.Controls[i];
            if (sibling is null || ReferenceEquals(sibling, excluded) || !sibling.Visible) continue;

            var bounds = sibling.Bounds;
            var inter = Rectangle.Intersect(bounds, source);
            if (inter.Width <= 0 || inter.Height <= 0) continue;

            try
            {
                using var slice = new Bitmap(inter.Width, inter.Height);
                // 目标矩形按控件原始尺寸偏移（负偏移即裁掉交集之外的部分），只渲染相交区域。
                sibling.DrawToBitmap(slice, new Rectangle(bounds.X - inter.X, bounds.Y - inter.Y, bounds.Width, bounds.Height));
                graphics.DrawImage(slice, inter.X, inter.Y);
            }
            catch (Exception ex)
            {
                FrontEndDrivenLog.Log("SkinDecorator_backdrop_sibling_failed",
                    new { type = sibling.GetType().Name, message = ex.Message });
            }
        }
    }

    /// <summary>快照生成期间的线程级重入闸（父级 owner-draw 不得再取快照）。</summary>
    [ThreadStatic]
    private static bool _capturingBackdrop;

    /// <summary>槽优先选择（唯一实现：<see cref="SurfaceSlotSelection"/>）。</summary>
    private static ResolvedMaterial? SelectMaterial(IReadOnlyDictionary<string, ResolvedMaterial>? materials)
        => SurfaceSlotSelection.SelectBackground(materials);

    /// <summary>缓存键（唯一实现：<see cref="SkinCacheKey"/>；含资产代，防换根后旧底图假命中）。</summary>
    private static string CacheKey(
        string fingerprint, Rectangle bounds, int dpi, SurfaceLightState? light, int backdropVersion)
        => SkinCacheKey.Build(
            fingerprint, bounds.Width, bounds.Height, dpi,
            light is { } state ? state.StaticCacheToken : null,
            backdropVersion, SkinPaintEngine.AssetGeneration);

    /// <summary>指纹读取面：Attach 期已写 <see cref="SkinState.MaterialKey"/>；缺失才补算并按状态缓存（禁每帧重算）。</summary>
    private static string FingerprintOf(SkinState state, ResolvedMaterial material)
        => state.MaterialKey ??= MaterialKey(material);

    /// <summary>材质指纹：含层参数摘要（同材质换色必须失效，否则换肤永不重绘）。</summary>
    private static string MaterialKey(ResolvedMaterial material)
        => SkinCacheKey.Fingerprint(material.MaterialId, material.FallbackUsed, SkinCacheKey.ParamDigest(material.Layers));

    private sealed class SkinState
    {
        internal ResolvedMaterial? Material { get; set; }
        internal string? MaterialKey { get; set; }

        /// <summary>族声明的边缘归属（Material = 材质自带 stroke；FrameHost = 由 <see cref="SurfaceFrameHost"/> 补边）。</summary>
        internal SurfaceEdgeMode EdgeMode { get; set; } = SurfaceEdgeMode.Material;
        internal PaintEventHandler? Handler { get; set; }
        internal Bitmap? Cache { get; set; }
        internal string? CacheKey { get; set; }
        internal int BackdropVersion { get; set; }
        internal int CacheBackdropVersion { get; set; }
        internal long LastBackdropTick { get; set; }
        internal bool SuppressPaint { get; set; }
        internal System.Windows.Forms.Timer? RefreshTimer { get; set; }
        internal SkinRenderInputs? Pinned { get; set; }

        /// <summary>帧序号（每帧 PaintControl 推进一次；异常帧也推进）。</summary>
        internal long Frame { get; set; }

        /// <summary>最近一帧结构化报告（静态 + 动态合并；异常帧为 Failed）。</summary>
        internal SkinPaintReport? LastReport { get; set; }

        /// <summary>最近一次静态底图报告（命中缓存时复用）。</summary>
        internal SkinPaintReport? BaseReport { get; set; }
    }
}
