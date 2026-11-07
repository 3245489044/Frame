#nullable enable

using System.Drawing;
using System.Windows.Forms;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 载体屏幕原点的唯一产出点：WinForms 控件的客户区左上角 + 客户区尺寸，换算到 DIP。
/// <para>所有锚点发布点（WinForms 采样 / HTML 推送）都必须用本方法取 <see cref="SurfaceCarrierFrame"/>，
/// 避免某处自行拼 origin、再把已经减过原点的局部坐标当屏幕坐标二次相减。</para>
/// </summary>
public static class SurfaceLightCarrierOrigin
{
    /// <summary>取控件的物理屏幕客户区（DIP）。</summary>
    public static SurfaceCarrierFrame Of(Control control)
    {
        var dpi = control.DeviceDpi > 0 ? control.DeviceDpi : 96;
        var scale = 96f / dpi;
        var origin = control.PointToScreen(Point.Empty);
        var client = control.ClientSize;
        return new SurfaceCarrierFrame(
            origin.X * scale,
            origin.Y * scale,
            client.Width * scale,
            client.Height * scale,
            dpi);
    }
}
