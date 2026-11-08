#nullable enable

using System;
using System.Drawing;
using Share.Conversation.Rendering.Surface;

namespace Share.SurfacePaint.Hosting;

/// <summary>
/// 单次绘制的动态输入快照（运行时光 + 背后面板快照），实际绘制与期望拟合共用同一份。
/// <para>生命周期契约：构建者拥有 <see cref="Backdrop"/>，用完由调用方 <see cref="Dispose"/>；
/// 绘制方只借用、不得释放（缓存位图不持有输入，避免跨帧复用已释放对象）。</para>
/// <para>消费方：<see cref="SkinDecorator"/>（真实绘制）/ <see cref="SkinPixelProbe"/>（期望拟合与同源冻结）。</para>
/// </summary>
public sealed class SurfaceRenderInputs : IDisposable
{
    private Bitmap? _backdrop;

    private SurfaceRenderInputs(SurfaceLightState? light, Bitmap? backdrop, int backdropVersion)
    {
        Light = light;
        _backdrop = backdrop;
        BackdropVersion = backdropVersion;
    }

    /// <summary>运行时光（材质不含 backdrop 时为 null ⇒ 静态外观）。</summary>
    public SurfaceLightState? Light { get; }

    /// <summary>背后快照（已裁到控件尺寸；null ⇒ 无宿主快照，绘制侧走 emulated 并显式记账）。</summary>
    public Bitmap? Backdrop => _backdrop;

    /// <summary>快照版本（缓存键/同源判定；0 表示本次无快照）。</summary>
    public int BackdropVersion { get; }

    public static SurfaceRenderInputs Create(SurfaceLightState? light, Bitmap? backdrop, int backdropVersion)
        => new(light, backdrop, backdropVersion);

    public void Dispose()
    {
        _backdrop?.Dispose();
        _backdrop = null;
    }
}
