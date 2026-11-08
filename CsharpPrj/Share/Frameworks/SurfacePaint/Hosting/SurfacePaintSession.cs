#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Share.Conversation.Rendering.Surface;
using Share.SurfacePaint.Ambient;
using Share.SurfacePaint.Diagnostics;
using Share.SurfacePaint.Engine;
using Share.SurfacePaint.Materials;
using UiBackend;
using UiBackend;

namespace Share.SurfacePaint.Hosting;

/// <summary>一帧绘制结果（帧序号 + 结构化报告 + 是否命中缓存）。</summary>
/// <param name="Frame">帧序号（每帧推进一次，异常帧也推进）。</param>
/// <param name="Report">本帧报告（静态 + 动态合并）。</param>
/// <param name="FromCache">静态底图是否命中缓存。</param>
public readonly record struct SurfaceFrameResult(long Frame, SurfacePaintReport Report, bool FromCache)
{
    /// <summary>空帧。</summary>
    public static readonly SurfaceFrameResult Empty = new(0, SurfacePaintReport.Empty, false);
}

/// <summary>
/// 单宿主绘制会话（**载体无关**）：材质 → 阶段计划 → 底图缓存 → 5 阶段落笔 → 报告。
/// <para>生命周期与 Paint 事件绑定留在载体；本类只负责「给定画布画一帧」。</para>
/// <para>输入（视口/背后快照/运行时光）全部经端口注入，禁 Control/Form/Timer。</para>
/// </summary>
public sealed class SurfacePaintSession : IDisposable
{
    private const int BackdropRefreshMs = 2000;

    private static readonly SurfacePaintTelemetry Telemetry = new(
        SurfaceRenderProfile.Begin, SurfaceRenderProfile.RecordLayer, SurfaceRenderProfile.RecordBlur);

    private readonly string _hostId;
    private readonly ISurfaceHost _host;
    private readonly ISurfaceBackdropSource? _backdrop;
    private readonly ISurfaceLightSource? _light;
    private readonly ISurfaceEnginePort? _engine;
    private readonly ISurfaceEventLog _log;

    private ResolvedMaterial? _material;
    private string? _materialKey;
    private SurfaceEdgeMode _edgeMode = SurfaceEdgeMode.Material;
    private Bitmap? _cache;
    private string? _cacheKey;
    private int _backdropVersion;
    private int _cacheBackdropVersion;
    private long _lastBackdropTick;
    private long _frame;
    private SurfacePaintReport? _lastReport;
    private SurfacePaintReport? _baseReport;
    private string? _decorMissing;
    private SurfaceRenderInputs? _pinned;

    /// <summary>构造会话。</summary>
    /// <param name="hostId">宿主标识（诊断/缓存键；同进程唯一）。</param>
    /// <param name="host">宿主端口。</param>
    /// <param name="backdrop">背后快照端口（可空 ⇒ 走近似并记账）。</param>
    /// <param name="light">运行时光端口（可空 ⇒ 静态外观）。</param>
    /// <param name="engine">引擎帧端口（可空 ⇒ 引擎帧路径关闭）。</param>
    /// <param name="log">事件日志端口（可空 ⇒ 静默）。</param>
    public SurfacePaintSession(
        string hostId,
        ISurfaceHost host,
        ISurfaceBackdropSource? backdrop = null,
        ISurfaceLightSource? light = null,
        ISurfaceEnginePort? engine = null,
        ISurfaceEventLog? log = null)
    {
        _hostId = string.IsNullOrEmpty(hostId) ? "surface-host" : hostId;
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _backdrop = backdrop;
        _light = light;
        _engine = engine;
        _log = log ?? NullSurfaceEventLog.Instance;
    }

    /// <summary>当前生效材质（诊断读取面）。</summary>
    public ResolvedMaterial? Material => _material;

    /// <summary>当前材质指纹（缓存/诊断）。</summary>
    public string? MaterialKey => _materialKey;

    /// <summary>最近一帧报告。</summary>
    public SurfacePaintReport LastReport => _lastReport ?? SurfacePaintReport.Empty;

    /// <summary>当前帧序号。</summary>
    public long Frame => _frame;

    /// <summary>是否需要运行时光（backdrop/specular 层）。</summary>
    public static bool NeedsLight(ResolvedMaterial? material)
        => material is not null && material.Layers.Any(layer =>
            layer.Primitive == "backdrop" || layer.Primitive == "specular");

    /// <summary>是否含背后快照层。</summary>
    public static bool HasBackdrop(ResolvedMaterial? material)
        => material is not null && material.Layers.Any(layer => layer.Primitive == "backdrop");

    /// <summary>材质指纹（含层参数摘要）。</summary>
    public static string FingerprintOf(ResolvedMaterial material)
        => SurfaceCacheKey.Fingerprint(material.MaterialId, material.FallbackUsed,
            SurfaceCacheKey.ParamDigest(material.Layers));

    /// <summary>挂载/切换材质（同指纹幂等）。</summary>
    public void Attach(ResolvedMaterial material, SurfaceEdgeMode edgeMode)
    {
        ArgumentNullException.ThrowIfNull(material);
        var key = FingerprintOf(material);
        _edgeMode = edgeMode;
        if (string.Equals(_materialKey, key, StringComparison.Ordinal)) return;
        _material = material;
        _materialKey = key;
    }

    /// <summary>冻结一帧输入（测试/探针；绘制期间不再自行解析时间/快照）。</summary>
    public IDisposable PinInputs(SurfaceRenderInputs inputs)
    {
        var previous = _pinned;
        _pinned = inputs;
        return new PinScope(this, previous);
    }

    /// <summary>卸载：释放底图与宿主 chrome 缓存。</summary>
    public void Release()
    {
        _cache?.Dispose();
        _cache = null;
        _cacheKey = null;
        _backdropVersion = 0;
        _cacheBackdropVersion = 0;
        _lastBackdropTick = 0;
        _material = null;
        _materialKey = null;
        _pinned = null;
        _baseReport = null;
        _lastReport = null;
        SurfaceAmbientScene.Release(_hostId);
    }

    /// <inheritdoc />
    public void Dispose() => Release();

    /// <summary>在宿主画布上绘制一帧（兼容入口：无窗口快照 ⇒ 走旧载体路径）。</summary>
    public SurfaceFrameResult Paint(Graphics graphics, Rectangle bounds)
        => Paint(graphics, bounds, snapshot: null);

    /// <summary>
    /// 在宿主画布上绘制一帧（**快照消费入口**）。
    /// <para>快照非空 ⇒ 不再请求引擎/采集背景/解析光；只消费窗口级帧事实与卡片帧。</para>
    /// <para>快照为空 ⇒ 保持旧路径（兼容未接入 Refresh 的宿主/测试）。</para>
    /// </summary>
    public SurfaceFrameResult Paint(Graphics graphics, Rectangle bounds, SurfaceFrameSnapshot? snapshot)
    {
        var material = _material;
        if (material is null || bounds.Width <= 0 || bounds.Height <= 0)
            return new SurfaceFrameResult(_frame, SurfacePaintReport.Empty, false);

        var viewport = _host.Viewport;
        if (!viewport.IsValid) return new SurfaceFrameResult(_frame, SurfacePaintReport.Empty, false);

        var pinned = _pinned;
        var card = snapshot is not null && snapshot.Cards.TryGetValue(_hostId, out var foundCard)
            ? foundCard
            : null;
        var baseLight = snapshot is not null
            ? snapshot.Light
            : pinned is { } frozen
                ? frozen.Light
                : NeedsLight(material) ? _light?.Resolve(viewport) : (SurfaceLightState?)null;

        // 结构宿主 = 页场宿主；普通卡若声明 pageField，则在自身矩形内消费页场切片（B3 Material）。
        var scope = _edgeMode == SurfaceEdgeMode.FrameHost
            ? SurfaceDecorScope.PageHost
            : (material.Decoration & SurfaceDecoration.PageField) != 0
                ? SurfaceDecorScope.Material
                : SurfaceDecorScope.Face;
        var plan = SurfaceDecorPlan.Resolve(material, scope, SurfaceAmbientScene.Enabled);
        if (plan.Missing.Length > 0 && !string.Equals(_decorMissing, plan.Missing, StringComparison.Ordinal))
        {
            _decorMissing = plan.Missing;
            _log.Write("surface.decor.undeclared", new
            {
                host = _hostId,
                material = material.MaterialId,
                detail = plan.Missing,
            });
        }

        // 背景快照（按控件/按帧）：仅在“无窗口快照且引擎可能接管”时采集；快照模式由窗口生产者统一采集。
        var now = Environment.TickCount64;
        var hostFacts = SurfaceHostFacts.Current;
        var wantsEngine = snapshot is null && _engine is not null && SurfaceEngineFramePump.Enabled
            && hostFacts.Composition.Mode == SurfaceCompositionMode.EngineComposition
            && hostFacts.DeclaredTier is >= GlassTier.T0;
        SurfaceRenderInputs? ownedInputs = null;
        var inputs = pinned;
        if (inputs is null && snapshot is null && wantsEngine
            && (_cache is null || now - _lastBackdropTick >= BackdropRefreshMs))
        {
            ownedInputs = BuildRenderInputs(viewport, baseLight, forceBackdrop: true);
            inputs = ownedInputs;
            _backdropVersion = inputs.BackdropVersion;
            _lastBackdropTick = now;
        }

        // 唯一读取面：光 / 背景 / 玻璃计划 / 所有权 / 降级。
        var facts = snapshot?.Facts ?? SurfaceRenderFactsReader.Read(TextureFrameOf(viewport, inputs));
        // 快照模式：页面场/chrome 与卡片共用同一窗口级光态（禁逐卡采样）。
        var lightContext = snapshot is null
            ? null
            : new SurfaceFrameLightContext(snapshot.Light, snapshot.Facts, snapshot.Generation);
        var ownership = facts.Ownership;
        var engineLight = inputs?.Light ?? baseLight;
        var engineFrame = card?.Frame;
        if (engineFrame is null && snapshot is null)
        {
            engineFrame = facts.EngineOwnsFace && engineLight is { HasValue: true }
                ? SurfaceEngineFramePump.TryFrame(
                    viewport, material, engineLight, SurfaceEngineFramePump.Quality, _engine!,
                    facts.Backdrop, facts.Glass.Spec, _log)
                : null;
        }

        var engineOwnsFace = snapshot is not null
            ? card?.EngineOwnsFace == true
            : facts.EngineOwnsFace;
        if (snapshot is null && engineOwnsFace && engineFrame is null)
        {
            // Gather 事实与帧泵事实不一致 ⇒ 按泵事实降级，并显式记账。
            ownership = SurfaceStageOwnership.Carrier;
            engineOwnsFace = false;
            _log.Write("surface.gather.fallback", new { host = _hostId, reason = "engine-frame-missing" });
        }
        else if (snapshot is null && !engineOwnsFace && engineFrame is not null)
        {
            // 玻璃层不可达 ⇒ 即使引擎能出帧也不接管。
            engineFrame = null;
        }

        if (snapshot is null && !facts.Backdrop.Available)
        {
            _log.Write("surface.gather.fallback", new { host = _hostId, reason = "backdrop-unavailable" });
        }

        var paintBegin = SurfaceRenderProfile.Begin();
        var baseReport = _baseReport;
        try
        {
            var backdropDue = snapshot is null && pinned is null && HasBackdrop(material)
                && (_cache is null || now - _lastBackdropTick >= BackdropRefreshMs);
            var backdropVersion = inputs?.BackdropVersion ?? _backdropVersion;
            var key = CacheKey(bounds, baseLight, backdropVersion);
            var hit = _cache is not null && !backdropDue
                      && string.Equals(_cacheKey, key, StringComparison.Ordinal);

            if (!engineOwnsFace)
            {
                if (!hit) baseReport = RebuildBase(viewport, bounds, inputs, baseLight, allowCarrierCapture: snapshot is null);
                if (baseReport is null) baseReport = SurfacePaintReport.Empty;
            }

            // 阶段 1：页面场（只有页场宿主承载；普通面的背景是自己的基底）。
            if (plan.Paints(SurfacePaintStage.PageField))
            {
                // 快照模式：页面场已由窗口生产者统一决策；载体不再自行请求引擎（避免每控件一帧）。
                SurfaceAmbientScene.PaintHostSurface(
                    viewport, _hostId, graphics, snapshot is null ? _engine : null, _log, lightContext);
            }

            SurfacePaintReport frameReport;
            if (engineOwnsFace)
            {
                // 阶段 2+3：面基底与动态层由同一引擎帧产出，宿主只贴图。
                graphics.DrawImageUnscaled(engineFrame!, bounds);
                frameReport = new SurfacePaintReport(1, 1, 0, 0, 0, new[] { "engine-frame" });
            }
            else
            {
                // 阶段 2：面基底（无静态层的材质底图是全透明，贴图即空操作）。
                if (_cache is not null) graphics.DrawImageUnscaled(_cache, bounds);

                var light = baseLight;
                var dynamicReport = SurfacePaintEngine.Paint(
                    graphics, bounds, material, null, light, SurfacePaintScope.Dynamic, Telemetry);
                frameReport = (baseReport ?? SurfacePaintReport.Empty).Merge(dynamicReport);
            }

            // 阶段 4：卡片 chrome（留白环/外发光/彩带/纹理），画在不透明面之上。
            if (plan.Paints(SurfacePaintStage.Chrome))
            {
                SurfaceAmbientScene.PaintPanelChrome(_hostId, viewport, graphics, bounds, material, lightContext);
            }

            // 阶段 5：结构宿主的 1px 内衬边（最后画，保证不被面或 chrome 盖掉）。
            if (plan.Paints(SurfacePaintStage.FrameEdge))
            {
                SurfaceFrameEdgePainter.TryDraw(graphics, bounds, _edgeMode, material, viewport.Dpi, out _);
            }

            _frame++;
            _lastReport = frameReport;
            SurfaceRenderProfile.RecordPaint(paintBegin, hit);
            return new SurfaceFrameResult(_frame, frameReport, hit);
        }
        catch (Exception ex)
        {
            _frame++;
            _lastReport = new SurfacePaintReport(1, 0, 0, 0, 1,
                new[] { "frame:" + ex.GetType().Name + ":" + ex.Message });
            SurfaceRenderProfile.RecordFailure();
            _log.Write("surface.paint.failed", new
            {
                host = _hostId,
                material = material.MaterialId,
                exception = ex.ToString(),
            });
            return new SurfaceFrameResult(_frame, _lastReport, false);
        }
        finally
        {
            ownedInputs?.Dispose();
        }
    }

    /// <summary>重建静态底图：backdrop（快照+模糊）+ fill + edge；specular 不进入底图。</summary>
    private SurfacePaintReport RebuildBase(
        SurfaceViewport viewport, Rectangle bounds, SurfaceRenderInputs? pinned, SurfaceLightState? baseLight,
        bool allowCarrierCapture = true)
    {
        var renderBegin = SurfaceRenderProfile.Begin();
        SurfaceRenderInputs? owned = null;
        var inputs = pinned;
        if (inputs is null && allowCarrierCapture && NeedsLight(_material))
        {
            owned = BuildRenderInputs(viewport, baseLight);
            inputs = owned;
            // 快照失败的帧保留上一张底图（否则会把 emulated 近似永久缓存，出现整栏变黑/变灰）。
            if (HasBackdrop(_material) && inputs.Backdrop is null && _cache is not null)
            {
                owned.Dispose();
                _lastBackdropTick = Environment.TickCount64;
                return _baseReport ?? SurfacePaintReport.Empty;
            }
        }

        var skipped = SurfacePaintReport.Empty;
        var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            skipped = SurfacePaintEngine.Paint(
                graphics, bounds, _material, inputs?.Backdrop, inputs?.Light, SurfacePaintScope.Static, Telemetry);
            if (skipped.Details.Count > 0)
            {
                _log.Write("surface.paint.skipped", new
                {
                    host = _hostId,
                    material = _material?.MaterialId,
                    skipped = string.Join(";", skipped.Details),
                });
            }
        }

        _cache?.Dispose();
        _cache = bitmap;
        _cacheBackdropVersion = inputs?.BackdropVersion ?? 0;
        if (pinned is null && inputs is not null) _backdropVersion = inputs.BackdropVersion;
        _cacheKey = CacheKey(bounds, baseLight, _cacheBackdropVersion);
        _lastBackdropTick = Environment.TickCount64;
        _baseReport = skipped;
        owned?.Dispose();
        SurfaceRenderProfile.RecordRender(renderBegin);
        return skipped;
    }

    /// <summary>快照 → 统一背景纹理帧（跨节点唯一形态；null ⇒ 无源）。</summary>
    private SurfaceTextureFrame? TextureFrameOf(SurfaceViewport viewport, SurfaceRenderInputs? inputs)
    {
        if (inputs?.Backdrop is not { } bitmap) return null;
        var coverage = new SurfaceLightCoverage(
            (float)viewport.OriginXDip, (float)viewport.OriginYDip, viewport.Width, viewport.Height);
        return new SurfaceTextureFrame(
            SurfaceTextureKind.ParentSnapshot,
            coverage,
            viewport.Dpi,
            (float)viewport.OriginXDip,
            (float)viewport.OriginYDip,
            inputs.BackdropVersion,
            bitmap,
            "bitmap",
            string.Empty,
            _hostId);
    }

    /// <summary>构建一帧输入（运行时光 + 背后快照）；调用方拥有结果。</summary>
    public SurfaceRenderInputs BuildRenderInputs(
        SurfaceViewport viewport, SurfaceLightState? light = null, bool forceBackdrop = false)
    {
        var resolvedLight = NeedsLight(_material) || forceBackdrop
            ? light ?? _light?.Resolve(viewport)
            : null;
        if (!HasBackdrop(_material) && !forceBackdrop) return SurfaceRenderInputs.Create(resolvedLight, null, 0);

        var capture = _backdrop?.Capture(viewport, allowCached: false);
        var version = capture?.Version ?? 0;
        return SurfaceRenderInputs.Create(resolvedLight, capture?.Image, version);
    }

    private string CacheKey(Rectangle bounds, SurfaceLightState? light, int backdropVersion)
        => SurfaceCacheKey.Build(
            FingerprintOf(_material!), bounds.Width, bounds.Height, _host.Viewport.Dpi,
            light is { } state ? state.StaticCacheToken : null,
            backdropVersion, SurfacePaintEngine.AssetGeneration);

    private sealed class PinScope : IDisposable
    {
        private SurfacePaintSession? _session;
        private readonly SurfaceRenderInputs? _previous;

        internal PinScope(SurfacePaintSession session, SurfaceRenderInputs? previous)
        {
            _session = session;
            _previous = previous;
        }

        public void Dispose()
        {
            if (_session is null) return;
            _session._pinned = _previous;
            _session = null;
        }
    }
}
