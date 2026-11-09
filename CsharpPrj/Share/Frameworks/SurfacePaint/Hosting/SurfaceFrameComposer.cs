#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Share.Conversation.Rendering.Surface;
using MathematicalAlgorithms.Surface;
using Share.SurfacePaint.Ambient;
using Share.SurfacePaint.Cpu;
using Share.SurfacePaint.Engine;
using SurfacePaint.Contracts;
using UiBackend;

namespace Share.SurfacePaint.Hosting;


/// <summary>
/// 窗口帧组合器（**唯一帧生产者**）：聚拢事实 → 整窗帧（页面场 + 每卡完整面 + 诊断）。
/// <para>同一帧只用一个后端；材质只从 <c>SurfacePlanBundle</c> 取；失败 ⇒ 整帧 Blocked。</para>
/// </summary>
public sealed class SurfaceFrameComposer
{
    private readonly IReadOnlyDictionary<SurfaceBackendKind, ISurfaceRenderBackend> _backends;
    private readonly ISurfaceEventLog _log;
    private long _generation;

    /// <summary>构造组合器（默认装配 CPU + 可选引擎后端）。</summary>
    /// <param name="engine">引擎帧端口（可空 ⇒ 只有 CPU 后端）。</param>
    /// <param name="log">事件日志端口（可空 ⇒ 静默）。</param>
    public SurfaceFrameComposer(ISurfaceEnginePort? engine, ISurfaceEventLog? log = null)
        : this(new CpuSurfaceBackend(), engine is null ? null : new EngineSurfaceBackend(engine, log), log)
    {
    }

    /// <summary>构造组合器（显式后端；测试/替换用）。</summary>
    /// <param name="cpu">CPU 后端（必填）。</param>
    /// <param name="engine">引擎后端（可空）。</param>
    /// <param name="log">事件日志端口。</param>
    public SurfaceFrameComposer(ISurfaceRenderBackend cpu, ISurfaceRenderBackend? engine, ISurfaceEventLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(cpu);
        _log = log ?? NullSurfaceEventLog.Instance;
        var backends = new Dictionary<SurfaceBackendKind, ISurfaceRenderBackend>
        {
            [SurfaceBackendKind.Cpu] = cpu,
        };
        if (engine is not null) backends[SurfaceBackendKind.Engine] = engine;
        _backends = backends;
    }

    /// <summary>组合一帧（可在后台线程执行；不触碰 Control）。</summary>
    /// <param name="input">窗口级输入（由 UI 线程采集）。</param>
    /// <param name="gather">本轮聚拢事实。</param>
    /// <param name="ct">取消令牌。</param>
    public SurfaceFrameSnapshot Compose(
        SurfaceFrameInput input, SurfaceGatherFrame gather, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(gather);

        var facts = gather.Facts ?? SurfaceRenderFactsReader.Read(input.Backdrop);
        var diagnostics = new List<string>(input.Degradations);
        foreach (var issue in gather.Plan.Degradations) diagnostics.Add(issue.Code);
        foreach (var degradation in facts.Degradations) diagnostics.Add(degradation);

        var backend = Pick(gather.Composition, gather.Budget, out var engineMissing);
        if (engineMissing) diagnostics.Add("backend-missing");

        // 页场是可选装饰：门户未开启/后端不可用 ⇒ 不生产、不阻断卡片。
        Bitmap? pageField = null;
        if (SurfaceAmbientScene.Enabled)
        {
            pageField = backend.RenderPageField(gather.Decor, input.Viewport, gather.Light, facts, gather.Budget);
            if (pageField is null) diagnostics.Add("page-field-unavailable");
        }

        var faces = new Dictionary<string, SurfaceCardFace>(StringComparer.Ordinal);
        foreach (var card in input.Cards.OrderBy(c => c.DepthHint).ThenBy(c => c.Key, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();

            var entry = gather.Decor.For(card.Key) ?? SurfaceDecorEntry.Missing(card.Key, card.MaterialSlot);
            if (entry.MissingDeclaration) diagnostics.Add("decor-undeclared:" + card.Key);

            // 嵌套/叶子：不产面（继承父面）——治层叠的唯一规则。
            if (entry.Inherits)
            {
                faces[card.Key] = SurfaceCardFace.Inherit(card, entry);
                continue;
            }

            // 光按卡片矩形重投影（锚点是屏幕固定 DIP）：同一窗口级光态不得原样复用给所有卡片。
            var cardLight = gather.Light?.ForCard(
                input.Viewport.HostOriginXDip + card.BoundsDip.X,
                input.Viewport.HostOriginYDip + card.BoundsDip.Y,
                card.BoundsDip.Width,
                card.BoundsDip.Height);

            var request = new SurfaceBackendRequest(
                card, entry, gather.Plan, cardLight, facts, input.Viewport, gather.Budget, input.Backdrop);
            var rendered = backend.TryRender(request, out var face, out var reason);
            var renderedKind = backend.Kind;

            // 单卡失败只降级该卡：先用 CPU 后端重试，仍失败则留空面并记账；禁把整窗判 Blocked。
            if ((!rendered || face is null) && backend.Kind != SurfaceBackendKind.Cpu)
            {
                var cpu = _backends[SurfaceBackendKind.Cpu];
                if (cpu.TryRender(request, out var cpuFace, out var cpuReason) && cpuFace is not null)
                {
                    face = cpuFace;
                    rendered = true;
                    renderedKind = SurfaceBackendKind.Cpu;
                    diagnostics.Add("card-face-fallback:" + card.Key);
                }
                else
                {
                    reason = string.IsNullOrEmpty(cpuReason) ? reason : cpuReason;
                }
            }

            if (!rendered || face is null)
            {
                diagnostics.Add("card-face-missing:" + card.Key + ":" + reason);
            }

            var material = gather.Plan.FindMaterial(entry.MaterialSlot);
            faces[card.Key] = new SurfaceCardFace(
                card.Key,
                card.BoundsDip,
                material?.MaterialId ?? entry.MaterialSlot,
                material is null ? entry.MaterialSlot : SurfacePaintSession.FingerprintOf(material),
                entry.Role,
                entry.Depth,
                face,
                Owned: face is not null && renderedKind == SurfaceBackendKind.Cpu,
                Inherited: false,
                Fingerprint: FaceFingerprint(card, entry, cardLight, material));
        }

        var generation = Interlocked.Increment(ref _generation);
        var snapshot = new SurfaceFrameSnapshot(
            input.WindowId,
            generation,
            input.Viewport,
            gather.Light,
            facts,
            pageField,
            faces,
            DegradationsDistinct(diagnostics),
            FrameFingerprint(input, gather, pageField, faces),
            Environment.TickCount64)
        {
            Pointer = gather.Pointer,
            AmbientEnabled = SurfaceAmbientScene.Enabled,
            QualityProfile = gather.Budget.Quality.ToString().ToLowerInvariant(),
            EngineReady = _backends.TryGetValue(SurfaceBackendKind.Engine, out var engineBackend)
                          && engineBackend.Status.IsReady,
        };

        _log.Write("surface.frame.composed", new
        {
            window = snapshot.WindowId,
            generation,
            cards = faces.Count,
            backend = backend.Kind.ToString().ToLowerInvariant(),
            ambient = snapshot.AmbientEnabled,
            quality = snapshot.QualityProfile,
            engineReady = snapshot.EngineReady,
            degradations = snapshot.Degradations.Count,
        });
        return snapshot;
    }

    /// <summary>兼容入口（无聚拢事实；显式降级为 CPU 路径）。</summary>
    /// <param name="input">窗口级输入。</param>
    /// <param name="ct">取消令牌。</param>
    public SurfaceFrameSnapshot Compose(SurfaceFrameInput input, CancellationToken ct = default)
        => Compose(input, SurfaceGatherFrame.Empty(input.Viewport) with
        {
            Light = input.Light,
            Pointer = input.Pointer,
        }, ct);

    private ISurfaceRenderBackend Pick(
        SurfaceCompositionCapability composition, SurfaceFrameBudget budget, out bool engineMissing)
    {
        engineMissing = composition.Mode == SurfaceCompositionMode.EngineComposition;
        if (engineMissing
            && budget.Quality != SurfaceFrameQuality.Minimal
            && _backends.TryGetValue(SurfaceBackendKind.Engine, out var engine)
            && engine.Status.IsReady)
        {
            engineMissing = false;
            return engine;
        }

        return _backends[SurfaceBackendKind.Cpu];
    }

    private static string FaceFingerprint(
        SurfaceCardGeometry card, SurfaceDecorEntry entry, SurfaceLightState? light, ResolvedMaterial? material)
    {
        var builder = new StringBuilder(96);
        builder.Append(card.Key).Append('|')
            .Append(card.BoundsDip.Width).Append('x').Append(card.BoundsDip.Height)
            .Append('|').Append(entry.MaterialSlot)
            .Append('|').Append(entry.Role).Append('|').Append(entry.Depth)
            // 材质参数摘要必须进入指纹：仅用槽名会让「同槽改参数」被判 Unchanged 而不刷新。
            .Append('|').Append(material is null ? "no-material" : SurfacePaintSession.FingerprintOf(material))
            .Append('|').Append(light is { } value ? value.CacheToken : "no-light");
        return SurfaceFrameContract.Fingerprint(builder.ToString());
    }

    private static string FrameFingerprint(
        SurfaceFrameInput input,
        SurfaceGatherFrame gather,
        Bitmap? pageField,
        IReadOnlyDictionary<string, SurfaceCardFace> faces)
    {
        var builder = new StringBuilder(256);
        builder.Append(input.WindowId).Append('|')
            .Append(input.Viewport.Width).Append('x').Append(input.Viewport.Height)
            .Append('@').Append(input.Viewport.Dpi)
            .Append('|').Append(gather.Light is { } light ? light.CacheToken : "no-light")
            .Append('|').Append(gather.Plan.ProfileId)
            .Append('|').Append(pageField?.Width ?? 0).Append('x').Append(pageField?.Height ?? 0);

        foreach (var pair in faces.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Append('|').Append(pair.Key).Append(':').Append(pair.Value.Fingerprint);
        }

        return SurfaceFrameContract.Fingerprint(builder.ToString());
    }

    private static IReadOnlyList<string> DegradationsDistinct(List<string> values)
    {
        if (values.Count == 0) return Array.Empty<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(values.Count);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (seen.Add(value)) result.Add(value);
        }

        return result;
    }
}
