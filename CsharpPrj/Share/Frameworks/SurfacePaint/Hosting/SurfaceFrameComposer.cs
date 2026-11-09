#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using MathematicalAlgorithms.Surface;
using Share.Conversation.Rendering.Surface;
using Share.SurfacePaint.Engine;

namespace Share.SurfacePaint.Hosting;


/// <summary>
/// 窗口帧组合器（**唯一帧生产者**）：渲染事实 → 卡片预渲染 → <see cref="SurfaceFrameSnapshot"/>。
/// <para>纪律：</para>
/// <list type="bullet">
/// <item>只在材质疑似需要光/背景时才请求引擎（资格门；普通 Label 不再进 GPU）；</item>
/// <item>不直接读宿主/Registry；事实统一经 <see cref="SurfaceRenderFactsReader"/>；</item>
/// <item>失败不抛：记降级原因并把该卡片交回载体 CPU 路径；</item>
/// <item>不持有 Control；输入全部来自 <see cref="SurfaceFrameInput"/>（可后台执行）。</item>
/// </list>
/// </summary>
public sealed class SurfaceFrameComposer
{
    private readonly ISurfaceEnginePort? _engine;
    private readonly ISurfaceEventLog _log;
    private long _generation;

    /// <summary>构造组合器。</summary>
    /// <param name="engine">引擎帧端口（可空 ⇒ 不产引擎帧，全部交载体）。</param>
    /// <param name="log">事件日志端口（可空 ⇒ 静默）。</param>
    public SurfaceFrameComposer(ISurfaceEnginePort? engine, ISurfaceEventLog? log = null)
    {
        _engine = engine;
        _log = log ?? NullSurfaceEventLog.Instance;
    }

    /// <summary>组合一帧（可在后台线程执行；不触碰 Control）。</summary>
    /// <param name="input">窗口级输入（由 UI 线程采集）。</param>
    /// <param name="ct">取消令牌。</param>
    public SurfaceFrameSnapshot Compose(SurfaceFrameInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var cards = new Dictionary<string, SurfaceCardFrame>(StringComparer.Ordinal);
        var degradations = new List<string>(input.Degradations);
        SurfaceRenderFacts? facts = null;

        // 窗口级背景单次装配：所有卡片复用同一份事实/纹理（不再逐卡抓取）。
        var windowFacts = SurfaceRenderFactsReader.Read(input.Backdrop);
        // 端口就绪态前置：Missing/NotReady 一律不进引擎，避免“调用后返回回显被当协议”。
        var engineReady = _engine is not null && _engine.Status.IsReady;

        foreach (var card in input.Cards)
        {
            ct.ThrowIfCancellationRequested();

            var cardFacts = card.Backdrop is null ? windowFacts : SurfaceRenderFactsReader.Read(card.Backdrop);
            facts ??= cardFacts;
            var backdrop = card.Backdrop ?? input.Backdrop;

            // 资格门：只有材质本身需要光/背景才进引擎；composition 为 Engine 不等于所有控件都要 GPU。
            var needsEngine = SurfacePaintSession.NeedsLight(card.Material)
                              || SurfacePaintSession.HasBackdrop(card.Material);
            var ownsFace = needsEngine && cardFacts.EngineOwnsFace;

            Bitmap? frame = null;
            if (ownsFace && input.Light is { HasValue: true } && engineReady)
            {
                frame = SurfaceEngineFramePump.TryFrame(
                    card.Viewport,
                    card.Material,
                    input.Light,
                    SurfaceEngineFramePump.Quality,
                    _engine,
                    backdrop,
                    cardFacts.Glass.Spec,
                    _log);
            }

            if (ownsFace && frame is null)
            {
                ownsFace = false;
                degradations.Add("engine-frame-missing");
            }

            cards[card.Key] = new SurfaceCardFrame(
                card.Key,
                card.Viewport.Bounds,
                card.Material.MaterialId,
                SurfacePaintSession.FingerprintOf(card.Material),
                frame,
                ownsFace,
                CardFingerprint(card, frame, input.Light));
        }

        if (facts is null)
        {
            facts = windowFacts;
        }

        var generation = Interlocked.Increment(ref _generation);
        return new SurfaceFrameSnapshot(
            input.WindowId,
            generation,
            input.Viewport,
            input.Light,
            facts,
            PageField: null,
            cards,
            DegradationsDistinct(degradations),
            FrameFingerprint(input, cards),
            Environment.TickCount64);
    }

    private static string CardFingerprint(SurfaceCardRequest card, Bitmap? frame, SurfaceLightState? light)
    {
        var builder = new StringBuilder(96);
        builder.Append(card.Key).Append('|')
            .Append(card.Viewport.Width).Append('x').Append(card.Viewport.Height).Append('@').Append(card.Viewport.Dpi)
            .Append('|').Append(SurfacePaintSession.FingerprintOf(card.Material))
            .Append('|').Append(frame is null ? "carrier" : "engine")
            .Append('|').Append(light is { } value ? value.CacheToken : "no-light");
        return SurfaceFrameContract.Fingerprint(builder.ToString());
    }

    private static string FrameFingerprint(
        SurfaceFrameInput input, IReadOnlyDictionary<string, SurfaceCardFrame> cards)
    {
        var builder = new StringBuilder(256);
        builder.Append(input.WindowId).Append('|')
            .Append(input.Viewport.Width).Append('x').Append(input.Viewport.Height)
            .Append('@').Append(input.Viewport.Dpi)
            .Append('|').Append(input.Light is { } light ? light.CacheToken : "no-light");

        foreach (var pair in cards.OrderBy(pair => pair.Key, StringComparer.Ordinal))
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
