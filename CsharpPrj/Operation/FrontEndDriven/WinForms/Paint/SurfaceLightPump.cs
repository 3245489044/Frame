#nullable enable

using System;
using System.Windows.Forms;
using Share.Registry;
using Share.Conversation.Rendering.Surface;
using TConfig;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 光场通道泵（**进程内唯一生产者**）：低频调用 <c>Engine_SampleSurfaceLight</c>，
/// 解析后整帧发布到 <see cref="SurfaceLightRegistry"/>；两个载体只读同一份结果。
/// <para>没有引擎函数（宿主未加载引擎）⇒ 发布 null，载体保持声明值；不伪造动态光。</para>
/// </summary>
internal static class SurfaceLightPump
{
    private const int IntervalMs = 1000;
    private const int RetryTicks = 5;

    private static readonly object Gate = new();
    private static System.Windows.Forms.Timer? _timer;
    private static int _idleTicks;
    private static bool _pullInProgress;
    private static int _pixelScaleDpi = 96;
    private static Control? _anchor;

    /// <summary>首次需要光时惰性启动（必须在含消息循环的 UI 线程调用）。</summary>
    public static void EnsureStarted(Control anchor)
    {
        lock (Gate)
        {
            if (_timer is not null) return;
            _pixelScaleDpi = anchor.DeviceDpi > 0 ? anchor.DeviceDpi : 96;
            _anchor = anchor;

            _timer = new System.Windows.Forms.Timer { Interval = IntervalMs };
            _timer.Tick += (_, _) => Pull();
            _timer.Start();
        }
    }

    /// <summary>屏幕覆盖区（屏幕 DIP）；与 <See cref="Pull"/> 的换算同一口径，供无帧回落帧使用。</summary>
    internal static SurfaceLightCoverage ScreenCoverage(Control anchor)
    {
        var screen = SystemInformation.VirtualScreen;
        var dpi = anchor.DeviceDpi > 0 ? anchor.DeviceDpi : (_pixelScaleDpi > 0 ? _pixelScaleDpi : 96);
        var scale = 96f / dpi;
        return new SurfaceLightCoverage(
            screen.X * scale,
            screen.Y * scale,
            Math.Max(1f, screen.Width * scale),
            Math.Max(1f, screen.Height * scale));
    }

    private static void Pull()
    {
        if (_pullInProgress) return;
        _pullInProgress = true;
        try
        {
            if (RegistryCoordinator.Registry is not ITrConfigRegistry registry
                || !registry.TryGetDelegate("Engine_SampleSurfaceLight", out var function)
                || function is not Func<int, int, int, int, string, string> sample)
            {
                DegradeIfIdle();
                return;
            }

            _idleTicks = 0;
            var screen = SystemInformation.VirtualScreen;
            // E-25：跨屏移动后 DPI 可能变化；每次拉取取当前值，不再只用启动时快照。
            var dpi = _anchor is { IsDisposed: false } anchor && anchor.DeviceDpi > 0
                ? anchor.DeviceDpi
                : _pixelScaleDpi;
            _pixelScaleDpi = dpi;
            var scale = 96f / dpi;
            var x = (int)Math.Round(screen.X * scale);
            var y = (int)Math.Round(screen.Y * scale);
            var width = Math.Max(1, (int)Math.Round(screen.Width * scale));
            var height = Math.Max(1, (int)Math.Round(screen.Height * scale));

            var raw = sample(x, y, width, height, string.Empty);
            var frame = SurfaceLightFrameCodec.Parse(raw);
            if (frame is null)
            {
                // 原因码 port-parse-null：端口已注册且被调用，但快照被引擎明示不可用（diag）或格式不符。
                SkinDiagnostics.RecordLightDegraded("port-parse-null", Clip(raw, 160));
            }
            SurfaceLightRegistry.Publish(frame);
        }
        catch (Exception ex)
        {
            // 原因码 port-exception：端口调用本身抛出（跨 ALC 类型/序列化等）。
            SkinDiagnostics.RecordLightDegraded("port-exception", ex.GetType().Name + ":" + ex.Message);
            // 通道异常只能降级为「无帧」，不得让绘制线程承担光计算。
            DegradeIfIdle();
        }
        finally
        {
            _pullInProgress = false;
        }
    }

    /// <summary>无帧（端口缺失/调用抛错）退化：连续 <see cref="RetryTicks"/> 次后发布 null（载体保持声明值）。</summary>
    private static void DegradeIfIdle()
    {
        _idleTicks++;
        if (_idleTicks < RetryTicks) return;
        _idleTicks = 0;
        SurfaceLightRegistry.Publish(null);
    }

    /// <summary>诊断用：截断引擎快照，避免一次性日志被整帧 cells 撑爆。</summary>
    private static string Clip(string? value, int limit)
    {
        if (value is null) return "<null>";
        return value.Length <= limit ? value : value.Substring(0, limit) + "...";
    }
}
