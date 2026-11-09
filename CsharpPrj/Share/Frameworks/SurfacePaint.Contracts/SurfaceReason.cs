#nullable enable

namespace SurfacePaint.Contracts;

/// <summary>
/// 表面渲染失败/降级原因码单源（受控词）。
/// <para>纪律：实现层只返回 <see cref="SurfaceReason"/>，日志/门禁按 <see cref="SurfaceReasonCode"/> 自动校验；禁再手写同义字面量。</para>
/// </summary>
public enum SurfaceReason
{
    /// <summary>无原因（成功/不适用）。</summary>
    None,

    /// <summary>端口未装配。</summary>
    PortMissing,

    /// <summary>端口已装配但依赖未就绪。</summary>
    PortNotReady,

    /// <summary>端口调用抛出异常（跨 ALC 类型/序列化等）。</summary>
    PortCallException,

    /// <summary>端口返回空响应（回显/未实现）。</summary>
    PortEmpty,

    /// <summary>响应不是合法协议 JSON。</summary>
    ResponseParseError,

    /// <summary>绘制项数量超过协议上限。</summary>
    ItemOverflow,

    /// <summary>响应无像素共享内存。</summary>
    NoPixels,

    /// <summary>像素共享内存不可读/长度非法。</summary>
    PixelsUnreadable,

    /// <summary>引擎帧缺失（降级到载体 CPU 路径）。</summary>
    EngineFrameMissing,

    /// <summary>背后快照不可用。</summary>
    BackdropUnavailable,

    /// <summary>窗口快照采集失败。</summary>
    WindowCaptureUnavailable,

    /// <summary>窗口帧视图声明不可用。</summary>
    FrameViewUnavailable,

    /// <summary>帧刷新失败。</summary>
    FrameRefreshFailed,

    /// <summary>装饰声明缺失。</summary>
    DecorUndeclared,
}

/// <summary><see cref="SurfaceReason"/> → 稳定字符串码（唯一映射点）。</summary>
public static class SurfaceReasonCode
{
    /// <summary>取稳定码（None ⇒ 空串，便于日志省略）。</summary>
    public static string Of(SurfaceReason reason) => reason switch
    {
        SurfaceReason.None => string.Empty,
        SurfaceReason.PortMissing => "port-missing",
        SurfaceReason.PortNotReady => "port-not-ready",
        SurfaceReason.PortCallException => "port-call-exception",
        SurfaceReason.PortEmpty => "port-empty",
        SurfaceReason.ResponseParseError => "response-parse-error",
        SurfaceReason.ItemOverflow => "item-overflow",
        SurfaceReason.NoPixels => "no-pixels",
        SurfaceReason.PixelsUnreadable => "pixels-unreadable",
        SurfaceReason.EngineFrameMissing => "engine-frame-missing",
        SurfaceReason.BackdropUnavailable => "backdrop-unavailable",
        SurfaceReason.WindowCaptureUnavailable => "window-capture-unavailable",
        SurfaceReason.FrameViewUnavailable => "surface-frame-view-unavailable",
        SurfaceReason.FrameRefreshFailed => "surface-refresh-failed",
        SurfaceReason.DecorUndeclared => "surface.decor.undeclared",
        _ => reason.ToString(),
    };
}
