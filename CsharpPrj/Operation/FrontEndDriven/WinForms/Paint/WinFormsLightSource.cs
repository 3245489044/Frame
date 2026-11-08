#nullable enable

using System;
using System.Windows.Forms;
using Share.Conversation.Rendering.Surface;
using Share.SurfacePaint.Hosting;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// WinForms 运行时光来源（<see cref="ISurfaceLightSource"/> 唯一实现）：
/// 只从全局光场 <see cref="SurfaceLightRegistry.Current"/> 采样；没有帧 ⇒ null（载体保持声明值）。
/// </summary>
internal sealed class WinFormsLightSource : ISurfaceLightSource
{
    private readonly Control _control;
    private bool _fallbackLogged;

    internal WinFormsLightSource(Control control) => _control = control;

    /// <summary>
    /// 测试/诊断用可控光源（null ⇒ 按光场解析）。只替换动态光解析，不改变静态底光的分钟量化。
    /// </summary>
    internal static Func<Control, SurfaceLightState?>? Override { get; set; }

    /// <inheritdoc />
    public SurfaceLightState? Resolve(SurfaceViewport viewport)
    {
        if (Override is { } over) return over(_control);

        SurfaceLightPump.EnsureStarted(_control);
        var current = SurfaceLightRegistry.Current;
        if (current is null)
        {
            if (!_fallbackLogged)
            {
                _fallbackLogged = true;
                SkinDiagnostics.RecordLightDegraded("engine-frame-unavailable", "SurfaceLightRegistry.Current=null");
            }

            return null;
        }

        var carrier = SurfaceLightCarrierOrigin.Of(_control);
        var probe = new SurfaceLightProbe(
            carrier.ScreenXDip,
            carrier.ScreenYDip,
            carrier.WidthDip,
            carrier.HeightDip,
            ZOrder: 0,
            Dpi: carrier.Dpi);
        var sample = SurfaceLightPort.Sample(current, probe);
        if (sample.HasValue && !sample.SpotInsideBounds)
        {
            // 越界只记账、不夹取：光斑允许落在面板之外，是否受光由 Shade 判定。
            SkinDiagnostics.RecordCarrierOutOfBounds(CarrierId(), sample.SpotBoundsDetail);
        }

        if (sample.HasValue)
        {
            SkinDiagnostics.RecordCarrierProjection(
                CarrierId(),
                current.Anchor,
                carrier,
                new SurfaceLightCarrierPoint(
                    sample.SpotLocalX, sample.SpotLocalY, sample.SpotInsideBounds, sample.SpotBoundsDetail));
        }

        return sample.HasValue ? SurfaceLightState.From(sample) : null;
    }

    private string CarrierId()
        => "winforms:" + (string.IsNullOrEmpty(_control.Name) ? _control.GetType().Name : _control.Name);
}
