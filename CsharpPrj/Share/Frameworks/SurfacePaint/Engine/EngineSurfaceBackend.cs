#nullable enable

using System;
using System.Drawing;
using Share.Conversation.Rendering.Surface;
using Share.SurfacePaint.Ambient;
using Share.SurfacePaint.Hosting;
using Share.SurfacePaint.Materials;
using SurfacePaint.Contracts;
using UiBackend;

namespace Share.SurfacePaint.Engine;

/// <summary>
/// 引擎渲染后端（包装既有帧泵）：就绪才可调用；未就绪 ⇒ <see cref="PortStatus"/> 非 Ready。
/// <para>页面场走环境层的引擎路径（<c>TryFrameRaw</c>）；卡片面走 <see cref="SurfaceEngineFramePump.TryFrame"/>。</para>
/// </summary>
public sealed class EngineSurfaceBackend : ISurfaceRenderBackend
{
    private readonly ISurfaceEnginePort _port;
    private readonly ISurfaceEventLog _log;

    /// <summary>构造后端。</summary>
    /// <param name="port">引擎端口（只读就绪态）。</param>
    /// <param name="log">事件日志（可空）。</param>
    public EngineSurfaceBackend(ISurfaceEnginePort port, ISurfaceEventLog? log = null)
    {
        _port = port ?? throw new ArgumentNullException(nameof(port));
        _log = log ?? NullSurfaceEventLog.Instance;
    }

    /// <summary>后端类别。</summary>
    public SurfaceBackendKind Kind => SurfaceBackendKind.Engine;

    /// <summary>端口就绪态（直接取端口事实，不缓存猜测）。</summary>
    public PortStatus Status => _port.Status;

    /// <inheritdoc />
    public Bitmap? RenderPageField(
        SurfaceDecorBundle decor,
        SurfaceViewport viewport,
        SurfaceLightState? light,
        SurfaceRenderFacts facts,
        SurfaceFrameBudget budget)
    {
        if (!viewport.IsValid || !_port.Status.IsReady) return null;

        var bitmap = new Bitmap(viewport.Width, viewport.Height);
        try
        {
            if (viewport.Dpi > 0) bitmap.SetResolution(viewport.Dpi, viewport.Dpi);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            SurfaceAmbientScene.PaintHostSurface(
                viewport, "pagefield:" + viewport.Width + "x" + viewport.Height, graphics,
                _port, _log, new SurfaceFrameLightContext(light, facts, 0));
            return bitmap;
        }
        catch (Exception)
        {
            bitmap.Dispose();
            return null;
        }
    }

    /// <inheritdoc />
    public bool TryRender(SurfaceBackendRequest request, out Bitmap? face, out string reason)
    {
        face = null;
        reason = string.Empty;
        if (!_port.Status.IsReady)
        {
            reason = SurfaceReasonCode.Of(SurfaceReason.PortNotReady);
            return false;
        }

        var material = request.Plan.FindMaterial(request.Decor.MaterialSlot)
                       ?? SurfaceMaterialCatalog.Global;
        if (material is null)
        {
            reason = "material-missing";
            return false;
        }

        // 无光 ≠ 不可渲染：泵的 ToLight 会产出零光帧（引擎照常出基底像素）。
        var card = request.Card;
        var cardViewport = request.Viewport.Card(card.BoundsDip);

        var frame = SurfaceEngineFramePump.TryFrame(
            cardViewport, material, request.Light,
            request.Budget.Quality.ToString().ToLowerInvariant(), _port,
            request.Backdrop, request.Facts?.Glass.Spec, _log);
        if (frame is null)
        {
            reason = string.IsNullOrEmpty(SurfaceEngineFramePump.LastReason)
                ? "engine-frame-missing"
                : SurfaceEngineFramePump.LastReason;
            return false;
        }

        face = frame;
        return true;
    }
}
