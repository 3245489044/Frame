#nullable enable

using System;
using System.Drawing;
using System.Linq;
using Share.Conversation.Rendering.Surface;
using UiBackend;

namespace Share.SurfacePaint.Hosting;

/// <summary>一帧绘制结果（帧序号 + 结构化报告 + 是否命中缓存）。</summary>
/// <param name="Frame">帧序号（每帧推进一次，异常帧也推进）。</param>
/// <param name="Report">本帧报告。</param>
/// <param name="FromCache">是否命中缓存（贴图路径恒 false）。</param>
public readonly record struct SurfaceFrameResult(long Frame, SurfacePaintReport Report, bool FromCache)
{
    /// <summary>空帧。</summary>
    public static readonly SurfaceFrameResult Empty = new(0, SurfacePaintReport.Empty, false);
}

/// <summary>
/// 单宿主绘制会话（**载体无关的贴图入口**）：只消费窗口帧快照中的卡片完整面。
/// <para>2026-11-09 B4：合成收敛到 <see cref="SurfaceFrameComposer"/>；本类不再解析材质/采样光/请求引擎/建底图缓存。</para>
/// <para>快照缺失 ⇒ 不画像素并显式记账（禁静默回落第二套绘制路径）。</para>
/// </summary>
public sealed class SurfacePaintSession : IDisposable
{
    /// <summary>背板重建节流（毫秒；载体背板采集间隔引用本值）。</summary>
    public const int BackdropRefreshMs = 2000;

    private readonly string _hostId;
    private readonly ISurfaceHost _host;
    private readonly ISurfaceEventLog _log;
    private ResolvedMaterial? _material;
    private SurfaceEdgeMode _edgeMode = SurfaceEdgeMode.Material;
    private long _frame;
    private SurfacePaintReport _lastReport = SurfacePaintReport.Empty;
    private bool _missingLogged;

    /// <summary>构造会话。</summary>
    /// <param name="hostId">宿主标识（与快照卡片键同源）。</param>
    /// <param name="host">宿主端口。</param>
    /// <param name="backdrop">保留参数（快照已含背景，本类不再采集）。</param>
    /// <param name="light">保留参数（快照已含光态，本类不再采样）。</param>
    /// <param name="engine">保留参数（引擎帧由 Composer 产出）。</param>
    /// <param name="log">事件日志端口（可空 ⇒ 静默）。</param>
    public SurfacePaintSession(
        string hostId,
        ISurfaceHost host,
        ISurfaceBackdropSource? backdrop = null,
        ISurfaceLightSource? light = null,
        ISurfaceEnginePort? engine = null,
        ISurfaceEventLog? log = null)
    {
        _ = backdrop;
        _ = light;
        _ = engine;
        _hostId = string.IsNullOrEmpty(hostId) ? "surface-host" : hostId;
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _log = log ?? NullSurfaceEventLog.Instance;
    }

    /// <summary>当前声明材质（诊断读取面；不参与绘制）。</summary>
    public ResolvedMaterial? Material => _material;

    /// <summary>当前材质指纹（诊断/缓存键片段；无材质 ⇒ null）。</summary>
    public string? MaterialKey => _material is null ? null : FingerprintOf(_material);

    /// <summary>最近一帧报告。</summary>
    public SurfacePaintReport LastReport => _lastReport;

    /// <summary>当前帧序号。</summary>
    public long Frame => _frame;

    /// <summary>材质指纹（含层参数摘要）。</summary>
    public static string FingerprintOf(ResolvedMaterial material)
        => SurfaceCacheKey.Fingerprint(material.MaterialId, material.FallbackUsed,
            SurfaceCacheKey.ParamDigest(material.Layers));

    /// <summary>挂载声明材质（只记账；绘制只读快照）。</summary>
    public void Attach(ResolvedMaterial? material, SurfaceEdgeMode edgeMode)
    {
        _material = material;
        _edgeMode = edgeMode;
    }

    /// <summary>兼容入口（B4 后为 no-op）：输入已固化在快照中。</summary>
    public IDisposable PinInputs(SurfaceRenderInputs inputs)
    {
        _ = inputs;
        return NoopScope.Instance;
    }

    /// <summary>兼容入口：返回当前光态（背景/视口不再由会话采集）。</summary>
    public SurfaceRenderInputs BuildRenderInputs(
        SurfaceViewport viewport, SurfaceLightState? light = null, bool forceBackdrop = false)
    {
        _ = viewport;
        _ = forceBackdrop;
        // 快照管线不再在会话内解析背景；光仍返回给探针/兼容调用方（不得静默丢光）。
        return SurfaceRenderInputs.Create(light, null, 0);
    }

    /// <summary>卸载。</summary>
    public void Release()
    {
        _material = null;
        _lastReport = SurfacePaintReport.Empty;
        _missingLogged = false;
    }

    /// <inheritdoc />
    public void Dispose() => Release();

    /// <summary>无快照兼容入口：显式走「快照缺失」路径（不画）。</summary>
    public SurfaceFrameResult Paint(Graphics graphics, Rectangle bounds)
        => Paint(graphics, bounds, snapshot: null);

    /// <summary>
    /// 在宿主画布上绘制一帧（**唯一贴图入口**）：只贴快照卡片面。
    /// </summary>
    /// <param name="graphics">宿主画布。</param>
    /// <param name="bounds">控件矩形（DIP 已换算为像素）。</param>
    /// <param name="snapshot">窗口帧快照（null/无本卡 ⇒ 不画并记账）。</param>
    public SurfaceFrameResult Paint(Graphics graphics, Rectangle bounds, SurfaceFrameSnapshot? snapshot)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return new SurfaceFrameResult(_frame, SurfacePaintReport.Empty, false);

        _ = _host.Viewport;

        // 页场是唯一由 Composer 生产的窗口级位图：按控件在宿主内的偏移切一片贴到本控件上。
        // 缺失/尺寸不符 ⇒ 不贴（不伪造），后续卡片面仍照常绘制。
        if (snapshot?.PageField is { } pageField)
        {
            var viewport = _host.Viewport;
            var source = new Rectangle(viewport.OffsetX, viewport.OffsetY, bounds.Width, bounds.Height);
            if (source.Width > 0 && source.Height > 0
                && source.X >= 0 && source.Y >= 0
                && source.Right <= pageField.Width && source.Bottom <= pageField.Height)
            {
                graphics.DrawImage(pageField, bounds, source, GraphicsUnit.Pixel);
            }
        }

        if (snapshot is not null && snapshot.Cards.TryGetValue(_hostId, out var card))
        {
            if (card.Inherited)
            {
                _frame++;
                _lastReport = new SurfacePaintReport(0, 0, 0, 0, 0, new[] { "snapshot-inherit" });
                return new SurfaceFrameResult(_frame, _lastReport, false);
            }

            if (card.Face is not null)
            {
                graphics.DrawImageUnscaled(card.Face, bounds.X, bounds.Y);
                _frame++;
                _lastReport = new SurfacePaintReport(1, 1, 0, 0, 0, new[] { "snapshot-face" });
                return new SurfaceFrameResult(_frame, _lastReport, false);
            }
        }

        if (!_missingLogged)
        {
            _missingLogged = true;
            _log.Write("surface.paint.snapshot-missing", new { host = _hostId, edge = _edgeMode.ToString() });
        }

        _frame++;
        _lastReport = new SurfacePaintReport(0, 0, 0, 0, 0, new[] { "snapshot-face-missing" });
        return new SurfaceFrameResult(_frame, _lastReport, false);
    }

    private sealed class NoopScope : IDisposable
    {
        internal static readonly NoopScope Instance = new();

        public void Dispose()
        {
        }
    }
}
