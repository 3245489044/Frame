#nullable enable

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using Share.Conversation.Rendering.Surface;
using Share.SurfacePaint.Ambient;
using Share.SurfacePaint.Hosting;
using Share.SurfacePaint.Materials;
using SurfacePaint.Contracts;
using UiBackend;

namespace Share.SurfacePaint.Cpu;

/// <summary>
/// CPU 渲染后端（GDI+ 离屏）：**始终可用**的兜底后端。
/// <para>消费 <c>surface-plan</c>（材质）+ <c>surface-decor</c>（阶段集）+ <c>surface-light</c>（高光）；</para>
/// <para>失败 ⇒ false + 原因码（禁静默空位图）。</para>
/// </summary>
public sealed class CpuSurfaceBackend : ISurfaceRenderBackend
{
    /// <summary>最近一次单卡合成的绘制报告（诊断/像素断言：引擎关闭时 specular 仍应落笔）。</summary>
    public static SurfacePaintReport LastReport { get; private set; } = SurfacePaintReport.Empty;

    /// <summary>后端类别。</summary>
    public SurfaceBackendKind Kind => SurfaceBackendKind.Cpu;

    /// <summary>CPU 后端始终就绪。</summary>
    public PortStatus Status => PortStatus.Ready(PortKind.Frame);

    /// <inheritdoc />
    public Bitmap? RenderPageField(
        SurfaceDecorBundle decor,
        SurfaceViewport viewport,
        SurfaceLightState? light,
        SurfaceRenderFacts facts,
        SurfaceFrameBudget budget)
    {
        if (!viewport.IsValid) return null;

        var bitmap = new Bitmap(viewport.Width, viewport.Height);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            // 页面场归 CPU 内部实现（引擎路径由 EngineSurfaceBackend 覆盖）。
            SurfaceAmbientScene.PaintHostSurface(
                viewport, "pagefield:" + viewport.Width + "x" + viewport.Height, graphics,
                enginePort: null, log: null, new SurfaceFrameLightContext(light, facts, 0));
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

        var card = request.Card;
        var width = Math.Max(1, card.BoundsDip.Width);
        var height = Math.Max(1, card.BoundsDip.Height);

        var material = request.Plan.FindMaterial(request.Decor.MaterialSlot)
                       ?? SurfaceMaterialCatalog.Global;
        if (material is null)
        {
            reason = "material-missing";
            return false;
        }

        var cardViewport = CardViewport(request.Viewport, card.BoundsDip);
        var bitmap = new Bitmap(width, height);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            var bounds = new Rectangle(0, 0, width, height);
            var backdrop = request.Backdrop?.Content as Image;
            var lightContext = new SurfaceFrameLightContext(request.Light, request.Facts, 0);
            var plan = request.Decor.Plan;

            // Base + Dynamic（含 specular 高光）。
            LastReport = SurfacePaintEngine.Paint(
                graphics, bounds, material, backdrop, request.Light, SurfacePaintScope.All);

            if (plan.Paints(SurfacePaintStage.Chrome))
            {
                SurfaceAmbientScene.PaintPanelChrome(
                    card.Key, cardViewport, graphics, bounds, material, lightContext);
            }

            if (plan.Paints(SurfacePaintStage.FrameEdge)
                && !SurfaceFrameEdgePainter.TryDraw(
                    graphics, bounds, SurfaceEdgeMode.FrameHost, material, cardViewport.Dpi, out var edgeReason)
                && !string.IsNullOrEmpty(edgeReason))
            {
                reason = "frame-edge:" + edgeReason;
            }

            face = bitmap;
            return true;
        }
        catch (Exception exception)
        {
            bitmap.Dispose();
            reason = "cpu-render-failed:" + exception.GetType().Name;
            return false;
        }
    }

    private static SurfaceViewport CardViewport(SurfaceViewport window, Rectangle boundsDip)
    {
        var scale = window.Scale;
        return new SurfaceViewport(
            Math.Max(1, boundsDip.Width),
            Math.Max(1, boundsDip.Height),
            window.Dpi,
            window.HostWidth,
            window.HostHeight,
            window.HostDpi,
            (int)Math.Round(boundsDip.X * scale),
            (int)Math.Round(boundsDip.Y * scale),
            window.HostOriginXDip,
            window.HostOriginYDip,
            window.HostOriginXDip + boundsDip.X,
            window.HostOriginYDip + boundsDip.Y);
    }
}
