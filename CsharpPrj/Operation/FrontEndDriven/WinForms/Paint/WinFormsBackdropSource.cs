#nullable enable

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FrontEndDriven.Diagnostics;
using Share.SurfacePaint.Hosting;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// WinForms 背后快照来源（<see cref="ISurfaceBackdropSource"/> 唯一实现）：
/// **只读窗口级背板**（<see cref="SurfaceWindowBackdropSource"/>，每窗口 ≤1 次/2s），按控件视口切片。
/// <para>边界：Paint 期禁止 <c>DrawToBitmap</c>/逐兄弟合成；采集失败 ⇒ 返回 null，调用侧降级并记账。</para>
/// </summary>
internal sealed class WinFormsBackdropSource : ISurfaceBackdropSource
{
    private readonly Control _control;

    internal WinFormsBackdropSource(Control control) => _control = control;

    /// <inheritdoc />
    public SurfaceBackdropCapture? Capture(SurfaceViewport viewport, bool allowCached)
    {
        if (!viewport.IsValid) return null;

        var form = _control.FindForm();
        if (form is null) return null;

        // 窗口级缓存是唯一像素来源（2s 节流 + 重入闸由 producer 自己保证）；此处不再自拍。
        var frame = SurfaceWindowBackdropSource.Capture(form);
        if (!frame.Available || frame.Content is not Bitmap window)
        {
            FrontEndDrivenLog.Log("SkinDecorator_backdrop_snapshot_failed", new
            {
                message = frame.Reason.Length > 0 ? frame.Reason : "window-capture-unavailable",
            });
            return null;
        }

        var target = new Rectangle(viewport.OffsetX, viewport.OffsetY, viewport.Width, viewport.Height);
        if (!new Rectangle(Point.Empty, window.Size).IntersectsWith(target))
        {
            FrontEndDrivenLog.Log("SkinDecorator_backdrop_snapshot_failed", new { message = "outside-window" });
            return null;
        }

        var crop = new Bitmap(target.Width, target.Height);
        using (var graphics = Graphics.FromImage(crop))
        {
            // 越界部分由 DrawImage 自然留黑（视口超窗只在布局异常时出现，不伪造）。
            graphics.DrawImage(window, new Rectangle(0, 0, target.Width, target.Height), target, GraphicsUnit.Pixel);
        }

        // 版本 = 内容摘要（窗口帧版本 + 切片坐标/尺寸 + DPI），不是调用序号：
        // 调用序号每次必变会让「缓存键 / 引擎帧 token」恒失效，静态底图永远重画。
        var version = HashCode.Combine(
            frame.Version, target.X, target.Y, target.Width, target.Height, viewport.Dpi) & 0x7FFFFFFF;
        if (version == 0) version = 1;
        return new SurfaceBackdropCapture(crop, version);
    }
}
