#nullable enable

namespace UiBackend;

/// <summary>
/// 整帧请求视口快照（聚拢域 <c>surface-viewport</c> 的唯一载荷）。
/// <para><see cref="HasValue"/>=false ⇒ 尚无真实帧请求；调用方必须按 Unknown 显式呈现，禁伪造 1920×1080。</para>
/// </summary>
/// <param name="Width">请求宽（设备像素）。</param>
/// <param name="Height">请求高（设备像素）。</param>
/// <param name="Dpi">请求 DPI。</param>
/// <param name="Quality">质量档（full/balanced/minimal）。</param>
/// <param name="HasValue">是否已有真实请求值。</param>
/// <param name="Reason">不可用原因码（HasValue=true 时为空串）。</param>
public sealed record SurfaceViewportState(
    int Width, int Height, int Dpi, string Quality, bool HasValue, string Reason)
{
    /// <summary>不可用占位（原因码可见；不猜测视口）。</summary>
    public static SurfaceViewportState Unavailable(string reason) => new(0, 0, 0, string.Empty, false, reason);
}

/// <summary>
/// 进程级视口发布点（写者 = <c>SurfaceFramePump</c>，读者 = 聚拢点；整值替换、读侧无锁）。
/// <para>唯一生产者：帧泵在组请求前发布实际 <c>bounds</c>/<c>DPI</c>/<c>quality</c>。</para>
/// </summary>
public static class SurfaceViewportRegistry
{
    private static volatile SurfaceViewportState _current = SurfaceViewportState.Unavailable("no-frame-yet");

    /// <summary>当前请求视口（永不为 null）。</summary>
    public static SurfaceViewportState Current => _current;

    /// <summary>发布真实请求视口（尺寸非法 ⇒ 显式不可用，不夹取成假值）。</summary>
    public static void Publish(int width, int height, int dpi, string? quality)
    {
        if (width <= 0 || height <= 0)
        {
            _current = SurfaceViewportState.Unavailable("invalid-viewport");
            return;
        }

        _current = new SurfaceViewportState(
            width, height, dpi > 0 ? dpi : 96, quality ?? string.Empty, true, string.Empty);
    }

    /// <summary>清空（宿主卸载/切档重来）。</summary>
    public static void Clear() => _current = SurfaceViewportState.Unavailable("no-frame-yet");
}
