#nullable enable

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Share.Conversation.Rendering.Surface;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 窗口级真实背景采集（**唯一 producer**）：每窗口 ≤1 次/2s，`WM_PRINT` 窗体自身
/// （含子控件、排除装饰层重入），产出 <see cref="SurfaceTextureFrame"/>（ParentSnapshot）。
/// <para>失败/节流不得伪造像素：返回 <see cref="SurfaceTextureFrame.Unavailable"/> 并带原因码。</para>
/// </summary>
internal static class SurfaceWindowBackdropSource
{
    /// <summary>采集间隔（毫秒）。</summary>
    internal const int CaptureIntervalMs = 2000;

    private const int WM_PRINT = 0x0317;
    private const int PRF_CLIENT = 0x04;
    private const int PRF_ERASEBKGND = 0x08;
    private const int PRF_CHILDREN = 0x10;

    private static readonly ConditionalWeakTable<Form, CaptureState> States = new();
    private static long _version;
    private static int _captures;
    private static int _unavailable;
    private static int _skipped;
    private static int _capturing;

    /// <summary>真实采集次数（诊断/断言）。</summary>
    internal static int CaptureCount => System.Threading.Volatile.Read(ref _captures);

    /// <summary>不可用次数（诊断/断言）。</summary>
    internal static int UnavailableCount => System.Threading.Volatile.Read(ref _unavailable);

    /// <summary>节流跳过次数（诊断/断言）。</summary>
    internal static int SkippedCount => System.Threading.Volatile.Read(ref _skipped);

    /// <summary>当前是否处于采集（装饰层重入闸：为 true 时控件自绘留白）。</summary>
    internal static bool IsCapturing => System.Threading.Volatile.Read(ref _capturing) == 1;

    /// <summary>采集一帧窗口背景（UI 线程；节流 + 重入闸）。</summary>
    internal static SurfaceTextureFrame Capture(Form form)
    {
        if (form is null || form.IsDisposed || !form.IsHandleCreated) return Unavailable("window-not-ready");
        var size = form.ClientSize;
        if (size.Width <= 0 || size.Height <= 0) return Unavailable("window-empty");

        var state = States.GetValue(form, static _ => new CaptureState());
        var now = Environment.TickCount64;
        if (state.Frame is not null
            && state.Width == size.Width && state.Height == size.Height && state.Dpi == form.DeviceDpi
            && now - state.At < CaptureIntervalMs)
        {
            System.Threading.Interlocked.Increment(ref _skipped);
            return state.Frame;
        }

        if (System.Threading.Interlocked.Exchange(ref _capturing, 1) == 1)
        {
            return state.Frame ?? Unavailable("capture-reentrant");
        }

        try
        {
            var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(form.BackColor);
                var hdc = graphics.GetHdc();
                try
                {
                    SendMessage(form.Handle, WM_PRINT, hdc, (IntPtr)(PRF_CLIENT | PRF_CHILDREN | PRF_ERASEBKGND));
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }
            }

            var frame = new SurfaceTextureFrame(
                SurfaceTextureKind.ParentSnapshot,
                new SurfaceLightCoverage(0f, 0f, size.Width, size.Height),
                form.DeviceDpi > 0 ? form.DeviceDpi : 96,
                0f,
                0f,
                System.Threading.Interlocked.Increment(ref _version),
                bitmap,
                "bitmap",
                string.Empty,
                "window-capture");
            state.Frame = frame;
            state.Width = size.Width;
            state.Height = size.Height;
            state.Dpi = form.DeviceDpi;
            state.At = now;
            System.Threading.Interlocked.Increment(ref _captures);
            return frame;
        }
        catch (Exception ex)
        {
            return Unavailable("window-capture-failed:" + ex.GetType().Name);
        }
        finally
        {
            System.Threading.Volatile.Write(ref _capturing, 0);
        }
    }

    /// <summary>清空缓存（卸载/测试）。</summary>
    internal static void Reset()
    {
        States.Clear();
        System.Threading.Volatile.Write(ref _capturing, 0);
        System.Threading.Volatile.Write(ref _captures, 0);
        System.Threading.Volatile.Write(ref _unavailable, 0);
        System.Threading.Volatile.Write(ref _skipped, 0);
    }

    private static SurfaceTextureFrame Unavailable(string reason)
    {
        System.Threading.Interlocked.Increment(ref _unavailable);
        return new SurfaceTextureFrame(
            SurfaceTextureKind.None,
            new SurfaceLightCoverage(0f, 0f, 1f, 1f),
            96, 0f, 0f, 0L, null, string.Empty, reason, "window-capture-unavailable");
    }

    private sealed class CaptureState
    {
        internal SurfaceTextureFrame? Frame { get; set; }
        internal int Width { get; set; }
        internal int Height { get; set; }
        internal int Dpi { get; set; }
        internal long At { get; set; }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
