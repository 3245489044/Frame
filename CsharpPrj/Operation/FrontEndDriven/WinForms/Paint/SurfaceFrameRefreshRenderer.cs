#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.Gather;
using Share.SurfacePaint.Hosting;
using Strategy.Refresh.Model;
using Strategy.Refresh.Ports;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>窗口帧渲染器（Refresh 的唯一 Renderer）：Gather 取数 → 组合快照 → 指纹裁决。</summary>
internal sealed class SurfaceFrameRefreshRenderer : IRefreshRenderer
{
    private readonly SurfaceFrameComposer _composer;
    private readonly GatherViewSpec _view;
    private readonly GatherHub _hub;
    private string? _lastFingerprint;

    internal SurfaceFrameRefreshRenderer(
        string slotKey, Func<SurfaceFrameInput?> capture, SurfaceFrameComposer composer,
        ISurfaceFrameGatherSource? viewSource = null)
    {
        SlotKey = slotKey;
        _composer = composer;
        // 声明单源：优先取宿主注入的声明面视图；缺失 ⇒ 显式回退（记录 surface-frame-view-unavailable）。
        _view = viewSource?.View ?? SurfaceFrameViewFallback.View;

        var registry = GatherRegistry.Build(new IGatherModule[]
        {
            new SurfaceFrameGatherModule(_view, capture),
        }, _view);
        if (!registry.IsClosed)
        {
            throw new InvalidOperationException(
                "surface-frame gather 未闭合：" + string.Join("；", registry.Issues));
        }

        _hub = new GatherHub(registry);
    }

    public string SlotKey { get; }

    public async Task<RefreshArtifact> RenderAsync(RefreshRequest request, CancellationToken ct = default)
    {
        try
        {
            var stages = await _hub.RunAsync(_view, request.Scopes, ct).ConfigureAwait(false);
            SurfaceFrameInput? input = null;
            foreach (var stage in stages)
            {
                if (stage.Values.TryGetValue("surface-frame-input", out var value)
                    && value is SurfaceFrameInput frameInput)
                {
                    input = frameInput;
                }
            }

            if (input is null)
            {
                return new RefreshArtifact(
                    Payload: null,
                    Outcome: RefreshOutcome.Blocked,
                    Fingerprint: "surface-window-unavailable");
            }

            var snapshot = _composer.Compose(input, ct);
            var ticket = _hub.Emit(_view, "surface-frame", snapshot);
            var payload = ticket.Payload;
            var fingerprint = snapshot.Fingerprint;
            if (string.Equals(_lastFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return RefreshArtifact.Unchanged(fingerprint, snapshot.Generation.ToString());
            }

            _lastFingerprint = fingerprint;
            return RefreshArtifact.Changed(payload, fingerprint, snapshot.Generation.ToString());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            FrontEndDriven.Diagnostics.FrontEndDrivenLog.Log(
                "surface.refresh.failed", new { slot = SlotKey, error = ex.Message });
            return RefreshArtifact.Failed("surface-refresh-failed");
        }
    }
}

/// <summary>Surface 帧聚拢模块（唯一取数入口）：只声明并提供一个 need，便于后续扩展域。</summary>
internal sealed class SurfaceFrameGatherModule : IGatherModule
{
    private readonly GatherViewSpec _view;
    private readonly Func<SurfaceFrameInput?> _capture;

    internal SurfaceFrameGatherModule(GatherViewSpec view, Func<SurfaceFrameInput?> capture)
    {
        _view = view;
        _capture = capture;
    }

    public string Name => "surface-frame-gather";

    public void Declare(GatherModuleBuilder builder)
    {
        builder.FromView(_view);
        builder.ProvideFor(_view, "surface-frame-input",
            (_, _) => Task.FromResult<object?>(_capture()));
    }
}

/// <summary>帧落地端口：快照写入 <see cref="Share.Composition.AssemblyFramework.KeyedSnapshotStore{TValue}"/>，由宿主触发失效。</summary>
internal sealed class SurfaceFrameRefreshSink : IRefreshSink
{
    private readonly Action<SurfaceFrameSnapshot> _deliver;

    internal SurfaceFrameRefreshSink(Action<SurfaceFrameSnapshot> deliver)
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
