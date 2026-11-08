#nullable enable

using System;
using System.Windows.Forms;
using MathematicalAlgorithms.Surface;
using Share.Conversation.Rendering.Surface;
using Share.Registry;
using Share.SurfacePaint.Engine;
using Share.SurfacePaint.Hosting;
using TConfig;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>WinForms 端口宿主（装配期设置一次；框架侧经端口读取）。</summary>
internal static class WinFormsSurfacePorts
{
    /// <summary>引擎帧端口（未装配 ⇒ 引擎帧路径关闭）。</summary>
    internal static ISurfaceEnginePort? Engine { get; set; }

    /// <summary>事件日志端口（默认空实现）。</summary>
    internal static ISurfaceEventLog Log { get; set; } = NullSurfaceEventLog.Instance;

    /// <summary>装配期唯一接线点（幂等）：引擎/日志端口 + 兼容层日志与字体缺省。</summary>
    internal static void Configure()
    {
        Engine = WinFormsSurfaceEnginePort.Instance;
        Log = WinFormsSurfaceLog.Instance;
        Share.SurfacePaint.Ambient.SurfaceAmbientLog.Log = Log;
        // 兼容层（Share.SurfacePaint.Compat）日志/字体缺省的唯一注入点：
        // 使下沉后的 SkinDiagnostics / SurfaceFontResolver 不反向依赖 FrontEndDriven。
        Share.SurfacePaint.Compat.SurfaceCompatLog.Sink = FrontEndDriven.Diagnostics.FrontEndDrivenLog.Log;
        Share.SurfacePaint.Compat.SurfaceFontDefaults.FallbackFamily = FrontEndDrivenConstants.DefaultFontFamily;
    }

    static WinFormsSurfacePorts() => Configure();
}

/// <summary>
/// 引擎帧端口实现：把 <c>Engine_SurfaceFrame</c> 注册函数适配为 <see cref="ISurfaceEnginePort"/>。
/// <para>跨 ALC 类型/序列化异常收敛为 false + 原因（禁抛回绘制线程）。</para>
/// </summary>
internal sealed class WinFormsSurfaceEnginePort : ISurfaceEnginePort
{
    /// <summary>单例（注册表寻址无状态）。</summary>
    internal static readonly WinFormsSurfaceEnginePort Instance = new();

    /// <inheritdoc />
    public bool TryRender(EngineSurfaceRequest request, out EngineSurfaceFrame? frame, out string reason)
    {
        frame = null;
        if (!TryPort(out var port))
        {
            reason = "port-missing";
            return false;
        }

        try
        {
            frame = EngineSurfaceBridge.TryRender(port, request, out reason);
            return frame is not null;
        }
        catch (Exception ex)
        {
            reason = "port-exception:" + ex.GetType().Name;
            return false;
        }
    }

    private static bool TryPort(out Func<string, string>? port)
    {
        port = null;
        if (RegistryCoordinator.Registry is not ITrConfigRegistry registry) return false;
        if (!registry.TryGetDelegate("Engine_SurfaceFrame", out var function)) return false;
        port = function as Func<string, string>;
        return port is not null;
    }
}

/// <summary>日志端口实现：转发到本工程 ModuleLog 门面。</summary>
internal sealed class WinFormsSurfaceLog : ISurfaceEventLog
{
    /// <summary>单例。</summary>
    internal static readonly WinFormsSurfaceLog Instance = new();

    /// <inheritdoc />
    public void Write(string eventId, object? data)
        => FrontEndDriven.Diagnostics.FrontEndDrivenLog.Log(eventId, data);
}

/// <summary>
/// 引擎帧泵（WinForms 适配）：实现单源 = <see cref="SurfaceEngineFramePump"/>（Share.SurfacePaint）。
/// </summary>
internal static class SurfaceFramePump
{
    internal static bool Enabled
    {
        get => SurfaceEngineFramePump.Enabled;
        set => SurfaceEngineFramePump.Enabled = value;
    }

    internal static string Quality
    {
        get => SurfaceEngineFramePump.Quality;
        set => SurfaceEngineFramePump.Quality = value;
    }

    internal static string LastReason => SurfaceEngineFramePump.LastReason;

    internal static System.Drawing.Bitmap? TryFrame(
        Control control, ResolvedMaterial? material, SurfaceLightState? light, string quality,
        UiBackend.SurfaceTextureFrame? backdrop = null, UiBackend.SurfaceGlassSpec? glass = null)
        => SurfaceEngineFramePump.TryFrame(
            SurfaceAmbient.ViewportOf(control), material, light, quality,
            WinFormsSurfacePorts.Engine ?? WinFormsSurfaceEnginePort.Instance, backdrop, glass,
            WinFormsSurfacePorts.Log);

    internal static System.Drawing.Bitmap? TryFrameRaw(
        Control control, System.Collections.Generic.IReadOnlyList<SurfaceDrawItem> items,
        string contentToken, SurfaceLightState? light, string quality)
        => SurfaceEngineFramePump.TryFrameRaw(
            SurfaceAmbient.ViewportOf(control), items, contentToken, light, quality,
            WinFormsSurfacePorts.Engine ?? WinFormsSurfaceEnginePort.Instance, null, WinFormsSurfacePorts.Log);
}

/// <summary>
/// 边框宿主（WinForms 兼容门面）：实现单源 = <see cref="SurfaceFrameEdgePainter"/>（Share.SurfacePaint）。
/// </summary>
public static class SurfaceFrameHost
{
    /// <summary>取可用于边框宿主的描边。</summary>
    public static BorderStroke? EdgeStrokeFor(ResolvedMaterial? material)
        => SurfaceFrameEdgePainter.EdgeStrokeFor(material);

    /// <summary>在控件客户区内绘制 1px 内衬边框。</summary>
    public static bool TryDraw(
        System.Drawing.Graphics graphics,
        System.Drawing.Rectangle bounds,
        SurfaceEdgeMode edgeMode,
        ResolvedMaterial? material,
        float dpi,
        out string? reason)
        => SurfaceFrameEdgePainter.TryDraw(graphics, bounds, edgeMode, material, dpi, out reason);
}
