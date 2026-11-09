#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.Gather;
using Share.Conversation.Rendering.Surface;
using Share.Conversation.Rendering.Surface.Gather;
using Share.SurfacePaint.Ambient;
using Share.SurfacePaint.Hosting;
using Strategy.Refresh.Model;
using Strategy.Refresh.Ports;
using SurfacePaint.Contracts;
using UiBackend;

namespace Strategy.SurfaceHosting;

/// <summary>
/// 窗口帧宿主子模块装配端口（**窄端口**）：声明单源与装配主体都在 Projects，渲染器只接收已闭合的子模块。
/// </summary>
public interface ISurfaceFrameModuleSource
{
    /// <summary>
    /// 按渲染器的**同一视图实例**装配宿主侧子模块（null ⇒ 不可用，渲染器落可见降级）。
    /// </summary>
    /// <param name="view">视图声明（必须与渲染器共用同一实例：组合去重依赖引用相等）。</param>
    /// <param name="context">帧作用域共享槽（plan → decor 桥接）。</param>
    IGatherModule? Create(GatherViewSpec view, SurfaceFrameContext context);
}

/// <summary>窗口帧渲染器（Refresh 的唯一 Renderer）：Gather 取数 → 组合快照 → 指纹裁决。</summary>
public sealed class SurfaceFrameRefreshRenderer : IRefreshRenderer
{
    private readonly SurfaceFrameComposer _composer;
    private readonly GatherViewSpec? _view;
    private readonly GatherHub? _hub;
    private readonly SurfaceFrameContext _context = new();
    private SurfaceFrameInput? _lastInput;
    private string? _lastFingerprint;

    /// <summary>构造渲染器。</summary>
    /// <param name="slotKey">刷新槽键。</param>
    /// <param name="capture">窗口采集（UI 线程执行）。</param>
    /// <param name="composer">唯一帧生产者。</param>
    /// <param name="viewSource">声明单源端口（null ⇒ 显式回退）。</param>
    /// <param name="moduleSource">宿主侧子模块装配端口（null ⇒ 闭合失败可见）。</param>
    /// <param name="pointerSource">指针来源（null ⇒ 显式不可用）。</param>
    public SurfaceFrameRefreshRenderer(
        string slotKey,
        Func<SurfaceFrameInput?> capture,
        SurfaceFrameComposer composer,
        ISurfaceFrameGatherSource? viewSource = null,
        ISurfaceFrameModuleSource? moduleSource = null,
        ISurfacePointerSource? pointerSource = null)
    {
        SlotKey = slotKey;
        _composer = composer;
        // 声明单源：宿主必须注入声明面视图与子模块；缺一 ⇒ 整帧 Blocked（B4-7：回退视图已删除）。
        _view = viewSource?.View;
        _hub = null;
        if (_view is null || moduleSource is null)
        {
            Share.SurfacePaint.Compat.SurfaceCompatLog.Log(
                "surface-frame-module-unavailable",
                new { reason = _view is null ? "declaration-missing" : "module-source-missing" });
            return;
        }

        var parts = new List<IGatherModule>
        {
            new SurfaceFrameGatherModule(_view, CaptureAndRemember(capture), _context),
            new SurfacePointerGatherModule(_view, pointerSource ?? UnavailablePointerSource.Instance),
            new SurfaceFoundationGatherModule(_view, BuildFoundationSources()),
            new SurfaceComposeGatherModule(
                _view, _context,
                () => _lastInput?.Viewport ?? SurfaceViewport.Empty,
                () => SurfaceAmbientScene.Enabled),
        };

        if (moduleSource.Create(_view, _context) is { } host)
        {
            parts.Insert(0, host);
        }
        else
        {
            Share.SurfacePaint.Compat.SurfaceCompatLog.Log(
                "surface-frame-module-unavailable",
                new { view = _view.View, reason = "host-module-missing" });
            _view = null;
            return;
        }

        var composite = new CompositeGatherModule("surface-window-frame", parts.ToArray());
        var registry = GatherRegistry.Build(new[] { composite }, _view);
        if (!registry.IsClosed)
        {
            throw new InvalidOperationException(
                "surface-frame gather 未闭合：" + string.Join("；", registry.Issues));
        }

        _hub = GatherHub.For(registry);
    }

    public string SlotKey { get; }

    public async Task<RefreshArtifact> RenderAsync(RefreshRequest request, CancellationToken ct = default)
    {
        try
        {
            _context.Reset();
            _lastInput = null;
            var view = _view;
            if (view is null || _hub is null)
            {
                return new RefreshArtifact(
                    Payload: SurfaceFrameSnapshot.Blocked(string.Empty, "surface-frame-module-unavailable"),
                    Outcome: RefreshOutcome.Blocked,
                    Fingerprint: "surface-frame-module-unavailable");
            }

            var stages = await _hub.RunAsync(view, request.Scopes, ct).ConfigureAwait(false);
            SurfaceFrameInput? input = null;
            SurfacePlanBundle? plan = null;
            SurfaceDecorBundle? decor = null;
            SurfaceLightFrame? lightFrame = null;
            SurfacePointerState pointer = SurfacePointerState.Unavailable;
            SurfaceFrameBudget? budget = null;
            SurfaceCompositionCapability? composition = null;
            SurfaceViewport viewport = SurfaceViewport.Empty;

            foreach (var stage in stages)
            {
                if (stage.Values.TryGetValue("surface-frame-input", out var value) && value is SurfaceFrameInput frameInput)
                    input = frameInput;
                if (stage.Values.TryGetValue("surface-plan", out var planValue) && planValue is SurfacePlanBundle planBundle)
                    plan = planBundle;
                if (stage.Values.TryGetValue("surface-decor", out var decorValue) && decorValue is SurfaceDecorBundle decorBundle)
                    decor = decorBundle;
                if (stage.Values.TryGetValue("surface-light", out var lightValue))
                {
                    if (lightValue is SurfaceLightFrame frame) lightFrame = frame;
                    else if (lightValue is SurfaceLightUnavailable unavailable)
                        Share.SurfacePaint.Compat.SurfaceCompatLog.Log(
                            "surface.light.unavailable", new { reason = unavailable.Reason });
                }
                if (stage.Values.TryGetValue("surface-pointer", out var pointerValue) && pointerValue is SurfacePointerState pointerState)
                    pointer = pointerState;
                if (stage.Values.TryGetValue("surface-frame-budget", out var budgetValue) && budgetValue is SurfaceFrameBudget frameBudget)
                    budget = frameBudget;
                if (stage.Values.TryGetValue("surface-composition", out var compValue) && compValue is SurfaceCompositionCapability capability)
                    composition = capability;
                if (stage.Values.TryGetValue("surface-viewport", out var viewportValue) && viewportValue is SurfaceViewport stageViewport)
                    viewport = stageViewport;
            }

            input ??= _lastInput;
            if (input is null)
            {
                return new RefreshArtifact(
                    Payload: null,
                    Outcome: RefreshOutcome.Blocked,
                    Fingerprint: "surface-window-unavailable");
            }

            if (!viewport.IsValid) viewport = input.Viewport;
            if (pointer.Equals(default(SurfacePointerState))) pointer = input.Pointer;

            var light = ResolveLight(lightFrame, viewport, pointer, input.Light);
            var facts = SurfaceRenderFactsReader.Read(input.Backdrop);
            var gather = new SurfaceGatherFrame(
                plan ?? SurfacePlanBundle.Empty,
                decor ?? SurfaceDecorBundle.Empty,
                light,
                pointer,
                budget ?? SurfaceHostFacts.Current.Budget,
                facts,
                composition ?? SurfaceHostFacts.Current.Composition,
                viewport);

            var snapshot = _composer.Compose(input, gather, ct);
            var fingerprint = snapshot.Fingerprint;
            if (string.Equals(_lastFingerprint, fingerprint, StringComparison.Ordinal))
            {
                // 未变化帧不会交付 ⇒ 必须主动释放本轮自有位图（页场/CPU 卡面），否则每次空转都泄漏。
                snapshot.ReleaseFrames();
                return RefreshArtifact.Unchanged(fingerprint, snapshot.Generation.ToString());
            }

            var ticket = _hub.Emit(view, "surface-frame", snapshot);
            var payload = ticket.Payload;

            _lastFingerprint = fingerprint;
            return RefreshArtifact.Changed(payload, fingerprint, snapshot.Generation.ToString());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Share.SurfacePaint.Compat.SurfaceCompatLog.Log(
                "surface.refresh.failed", new { slot = SlotKey, error = ex.Message });
            return RefreshArtifact.Failed("surface-refresh-failed");
        }
    }

    private Func<SurfaceFrameInput?> CaptureAndRemember(Func<SurfaceFrameInput?> capture)
        => () =>
        {
            var input = capture();
            _lastInput = input;
            return input;
        };

    private static SurfaceLightState? ResolveLight(
        SurfaceLightFrame? frame, SurfaceViewport viewport, SurfacePointerState pointer, SurfaceLightState? fallback)
    {
        // 已钉光优先（显式覆盖/测试）：无帧且 fallback 有值时保持历史行为。
        if (frame is null && fallback is not null) return fallback;

        var coverage = new SurfaceLightCoverage(
            (float)viewport.HostOriginXDip, (float)viewport.HostOriginYDip,
            (float)viewport.HostWidthDip, (float)viewport.HostHeightDip);

        // 指针光与环境光帧解耦：frame=null 时仍走 Apply 产出「仅指针」光态；
        // 只有「无帧 + 无指针 + 无覆盖区」才返回 null，并且显式记账，不再静默。
        var applied = SurfaceLightPointer.Apply(
            frame, coverage, viewport.HostDpi, pointer,
            viewport.HostWidthDip, viewport.HostHeightDip);
        if (applied is null)
        {
            Share.SurfacePaint.Compat.SkinDiagnostics.RecordLightDegraded(
                frame is null ? "pointer-unavailable" : "surface-light-unavailable",
                FormattableString.Invariant(
                    $"frame={(frame is null ? "null" : "present")};inside={(pointer.Inside ? "true" : "false")};"
                    + $"host={viewport.HostWidthDip:F0}x{viewport.HostHeightDip:F0};"
                    + $"fallback={(fallback is null ? "null" : "present")}"));
            return fallback;
        }

        if (frame is null)
        {
            // 正例证据：无环境光帧仍产出了指针光（HasValue=false，环境层保持声明值）。
            Share.SurfacePaint.Compat.SkinDiagnostics.RecordLightDegraded(
                "light-pointer-only",
                FormattableString.Invariant(
                    $"inside=true;host={viewport.HostWidthDip:F0}x{viewport.HostHeightDip:F0};"
                    + $"weight={applied.Value.PointerWeight:F2}"));
        }
        return applied;
    }

    private static SurfaceFoundationSources BuildFoundationSources()
        => new(
            static () => SurfaceHostFacts.Current.Composition,
            static () => SurfaceHostFacts.Current.Backdrop.FromTextureFrame(),
            static () => SurfaceHostFacts.Current.Budget,
            static () => SurfaceLightReader.Read().Frame,
            static () => SurfaceHostFacts.Glass(),
            SurfaceLightReader.LastReason,
            static () => SurfaceLightReader.Read());
}

/// <summary>无指针源（显式不可用；禁返回 null 冒充有值）。</summary>
internal sealed class UnavailablePointerSource : ISurfacePointerSource
{
    internal static readonly UnavailablePointerSource Instance = new();

    public PortStatus Status => PortStatus.Missing(PortKind.Pointer, "pointer-source-missing");

    public SurfacePointerState Read() => SurfacePointerState.Unavailable;
}

/// <summary>Surface 帧聚拢模块（唯一取数入口）：surface-frame-input 的**唯一 producer**。</summary>
internal sealed class SurfaceFrameGatherModule : IGatherModule
{
    private readonly GatherViewSpec _view;
    private readonly Func<SurfaceFrameInput?> _capture;
    private readonly SurfaceFrameContext _context;

    internal SurfaceFrameGatherModule(
        GatherViewSpec view, Func<SurfaceFrameInput?> capture, SurfaceFrameContext context)
    {
        _view = view;
        _capture = capture;
        _context = context;
    }

    public string Name => "surface-frame-gather";

    public void Declare(GatherModuleBuilder builder)
    {
        builder.FromView(_view);
        builder.ProvideFor(_view, "surface-frame-input", (_, _) =>
        {
            var input = _capture();
            if (input is not null) _context.SetCards(input.Cards);
            return Task.FromResult<object?>(input);
        });
    }
}

/// <summary>帧落地端口：快照写入 <c>KeyedSnapshotStore</c>，由宿主触发失效。</summary>
public sealed class SurfaceFrameRefreshSink : IRefreshSink
{
    private readonly Action<SurfaceFrameSnapshot> _deliver;

    public SurfaceFrameRefreshSink(Action<SurfaceFrameSnapshot> deliver)
    {
        _deliver = deliver;
    }

    public Task<DeliveryResult> DeliverAsync(RefreshDelivery delivery, CancellationToken ct = default)
    {
        if (delivery.Payload is not SurfaceFrameSnapshot snapshot)
        {
            return Task.FromResult(DeliveryResult.NotDelivered(
                new InvalidOperationException("Surface frame payload type mismatch: " + delivery.Payload?.GetType().Name)));
        }

        try
        {
            _deliver(snapshot);
            return Task.FromResult(DeliveryResult.Changed(delivery.Fingerprint));
        }
        catch (Exception ex)
        {
            return Task.FromResult(DeliveryResult.NotDelivered(ex));
        }
    }
}
