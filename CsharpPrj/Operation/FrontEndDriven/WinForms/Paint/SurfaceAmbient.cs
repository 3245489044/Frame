#nullable enable

using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using Share.Conversation.Rendering.Surface;
using Share.SurfacePaint.Ambient;
using Share.SurfacePaint.Hosting;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 页面装饰层（WinForms 兼容门面）：实现单源 = <see cref="SurfaceAmbientScene"/>（Share.SurfacePaint）。
/// <para>2026-11-08：光栅/缓存/光解算已下沉；本类只把 Control 坐标换算成 <see cref="SurfaceViewport"/>。</para>
/// </summary>
internal static class SurfaceAmbient
{
    /// <summary>装饰层是否可用。</summary>
    internal static bool Enabled => SurfaceAmbientScene.Enabled;

    /// <summary>最近一次解算的光态（只读观测；null = 尚未解算）。</summary>
    internal static SurfaceLightState? LastState => SurfaceAmbientScene.LastState;

    /// <summary>安装期写入口。</summary>
    internal static void Configure(SurfaceAmbientTokens tokens) => SurfaceAmbientScene.Configure(tokens);

    /// <summary>卸载。</summary>
    internal static void Reset() => SurfaceAmbientScene.Reset();

    /// <summary>释放某控件的 chrome 缓存。</summary>
    internal static void Release(Control? control)
        => SurfaceAmbientScene.Release(control is null ? null : IdOf(control));

    /// <summary>宿主背景（页面场切片）。</summary>
    internal static void PaintHostSurface(Control control, Graphics graphics)
        => SurfaceAmbientScene.PaintHostSurface(
            ViewportOf(control), IdOf(control), graphics, WinFormsSurfacePorts.Engine ?? WinFormsSurfaceEnginePort.Instance, WinFormsSurfacePorts.Log);

    /// <summary>卡片 chrome。</summary>
    internal static void PaintPanelChrome(Control control, Graphics graphics, Rectangle bounds, ResolvedMaterial material)
        => SurfaceAmbientScene.PaintPanelChrome(IdOf(control), ViewportOf(control), graphics, bounds, material);

    /// <summary>控件 → 视口（坐标换算唯一入口）。</summary>
    internal static SurfaceViewport ViewportOf(Control control)
    {
        var form = control.FindForm() ?? control;
        var formDpi = BorderUnits.NormalizeDpi(form.DeviceDpi);
        var dpi = BorderUnits.NormalizeDpi(control.DeviceDpi);
        var origin = control.PointToScreen(Point.Empty);
        var formOrigin = form.PointToScreen(Point.Empty);
        var scale = 96f / dpi;
        var formScale = 96f / formDpi;
        return new SurfaceViewport(
            control.ClientSize.Width,
            control.ClientSize.Height,
            dpi <= 0 ? 96 : (int)dpi,
            form.ClientSize.Width,
            form.ClientSize.Height,
            formDpi <= 0 ? 96 : (int)formDpi,
            origin.X - formOrigin.X,
            origin.Y - formOrigin.Y,
            formOrigin.X * formScale,
            formOrigin.Y * formScale,
            origin.X * scale,
            origin.Y * scale);
    }

    /// <summary>
    /// 控件稳定标识（缓存/诊断键，**唯一实现**）：实例序号 + 名称。
    /// <para>用实例序号而不是 <see cref="object.GetHashCode"/>：哈希可碰撞且不可读，
    /// 会让卡片键/诊断键错配（原先 LightSource 另有一套不含序号的命名）。</para>
    /// </summary>
    internal static string IdOf(Control control)
    {
        var box = Ids.GetValue(control, static _ => new IdBox(Interlocked.Increment(ref _idSeed)));
        var name = string.IsNullOrWhiteSpace(control.Name) ? control.GetType().Name : control.Name;
        return "winforms:" + box.Serial.ToString("X8", CultureInfo.InvariantCulture) + ":" + name;
    }

    private static readonly ConditionalWeakTable<Control, IdBox> Ids = new();
    private static int _idSeed;

    private sealed class IdBox
    {
        internal IdBox(int serial) => Serial = serial;

        internal int Serial { get; }
    }
}
