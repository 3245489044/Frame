#nullable enable

using System;
using System.Drawing;
using Share.Conversation.Rendering.Surface;

namespace Share.SurfacePaint.Hosting;

/// <summary>宿主提供的一帧背后快照（调用方拥有 Image，用完释放；框架只借用）。</summary>
/// <param name="Image">已裁到控件尺寸的背后快照。</param>
/// <param name="Version">快照版本（0 = 本次无快照；进静态底图缓存键）。</param>
public readonly record struct SurfaceBackdropCapture(Bitmap Image, int Version);

/// <summary>
/// 绘制宿主（每个控件一份实现）：提供视口、失效、属性面与（可选）双缓冲。
/// <para>这是框架与载体之间**唯一**的生命周期契约；框架内不得出现 Control/Form/Timer。</para>
/// </summary>
public interface ISurfaceHost
{
    /// <summary>当前视口快照。</summary>
    SurfaceViewport Viewport { get; }

    /// <summary>请求重绘。</summary>
    void Invalidate();

    /// <summary>把材质面写成宿主可承载的属性（不支持 ⇒ false + 原因，禁静默）。</summary>
    /// <param name="material">已解析材质。</param>
    /// <param name="tintArgb">旧样式 accent 叠加（null = 无）。</param>
    /// <param name="reason">失败原因（可空）。</param>
    bool TryApplyPropertyFace(ResolvedMaterial material, int? tintArgb, out string? reason);

    /// <summary>绘制前的宿主动作（如启用双缓冲）；返回可释放作用域。</summary>
    IDisposable? PrepareSurface();
}

/// <summary>背后快照来源（载体实现；框架只按需索取）。</summary>
public interface ISurfaceBackdropSource
{
    /// <summary>取一帧背后快照（可能命中缓存；失败 ⇒ null 且调用方记账）。</summary>
    SurfaceBackdropCapture? Capture(SurfaceViewport viewport, bool allowCached);
}

/// <summary>运行时光来源（载体实现：屏幕坐标/DPI → 采样）。</summary>
public interface ISurfaceLightSource
{
    /// <summary>解析当前光态（无帧 ⇒ null，载体保持声明值）。</summary>
    SurfaceLightState? Resolve(SurfaceViewport viewport);
}

/// <summary>框架事件日志端口（载体接 ModuleLog；默认空实现）。</summary>
public interface ISurfaceEventLog
{
    /// <summary>写一条结构化事件。</summary>
    void Write(string eventId, object? data);
}

/// <summary>空日志（默认；不产生任何开销）。</summary>
public sealed class NullSurfaceEventLog : ISurfaceEventLog
{
    /// <summary>单例。</summary>
    public static readonly NullSurfaceEventLog Instance = new();

    private NullSurfaceEventLog() { }

    /// <inheritdoc />
    public void Write(string eventId, object? data) { }
}

/// <summary>引擎帧端口（避免框架依赖 Share.Registry/TConfig；载体注入实现）。</summary>
public interface ISurfaceEnginePort
{
    /// <summary>渲染一帧；失败 ⇒ null 并给出原因。</summary>
    bool TryRender(
        Engine.EngineSurfaceRequest request,
        out Engine.EngineSurfaceFrame? frame,
        out string reason);
}
