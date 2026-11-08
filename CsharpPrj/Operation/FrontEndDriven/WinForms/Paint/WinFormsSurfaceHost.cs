#nullable enable

using System;
using System.Windows.Forms;
using FrontEndDriven.Diagnostics;
using Share.Conversation.Rendering.Surface;
using Share.SurfacePaint.Hosting;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// WinForms 宿主适配（<see cref="ISurfaceHost"/> 唯一实现）：视口/失效/属性面/双缓冲。
/// <para>坐标换算统一走 <see cref="SurfaceAmbient.ViewportOf"/>（唯一原点产出点）。</para>
/// </summary>
internal sealed class WinFormsSurfaceHost : ISurfaceHost
{
    private readonly Control _control;

    internal WinFormsSurfaceHost(Control control) => _control = control;

    /// <inheritdoc />
    public SurfaceViewport Viewport => SurfaceAmbient.ViewportOf(_control);

    /// <inheritdoc />
    public void Invalidate() => _control.Invalidate();

    /// <inheritdoc />
    public bool TryApplyPropertyFace(ResolvedMaterial material, int? tintArgb, out string? reason)
        => WinFormsPropertyProjector.TryApply(_control, material, tintArgb, out reason);

    /// <inheritdoc />
    public IDisposable? PrepareSurface()
    {
        if (!SkinDoubleBuffer.TryEnable(_control, out var reason) && reason is not null)
        {
            // 双缓冲失败只记账不抛（绘制仍正确，仅闪烁风险，禁静默）。
            FrontEndDrivenLog.Log("SkinDecorator_doublebuffer_unavailable",
                new { type = _control.GetType().Name, reason });
        }

        return null;
    }
}
