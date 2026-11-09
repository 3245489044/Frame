#nullable enable

using System;
using System.Windows.Forms;
using FrontEndDriven.Diagnostics;
using MathematicalAlgorithms.Surface;
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
    private static volatile SurfaceLightLook _look = SurfaceLightLook.Empty;
    private static long _ticks;
    private static long _published;
    private static long _lastPublishTick;
    private static string _lastReason = "not-started";
    private static int _uiThreadId;

    /// <summary>光泵可判定状态：是否起表、在哪个线程起表、tick/发布计数与最近判定原因。</summary>
    internal readonly record struct SurfaceLightPumpStatus(
        bool Started,
        int UiThreadId,
        long Ticks,
        long Published,
        string LastReason,
        long LastPublishTick);

    /// <summary>光泵状态快照（诊断/测试；无帧时用 <see cref="SurfaceLightPumpStatus.LastReason"/> 判定原因）。</summary>
    internal static SurfaceLightPumpStatus Status
    {
        get
        {
            lock (Gate)
            {
                return new SurfaceLightPumpStatus(
                    _timer is not null,
                    _uiThreadId,
                    System.Threading.Interlocked.Read(ref _ticks),
                    System.Threading.Interlocked.Read(ref _published),
                    _lastReason,
                    System.Threading.Interlocked.Read(ref _lastPublishTick));
            }
        }
    }

    /// <summary>引擎光 look 子集（装配期由生效快照登记；空 ⇒ 引擎按覆盖区派生）。</summary>
    public static SurfaceLightLook Look
    {
        get => _look;
        set => _look = value ?? SurfaceLightLook.Empty;
    }

    /// <summary>首次需要光时惰性启动（必须在含消息循环的 UI 线程调用）。</summary>
    public static void EnsureStarted(Control anchor)
    {
        var started = false;
        lock (Gate)
        {
            if (_timer is not null) return;
            _pixelScaleDpi = anchor.DeviceDpi > 0 ? anchor.DeviceDpi : 96;
            _anchor = anchor;
            _uiThreadId = Environment.CurrentManagedThreadId;
            _lastReason = "started";

            _timer = new System.Windows.Forms.Timer { Interval = IntervalMs };
            _timer.Tick += (_, _) => Pull();
            _timer.Start();
            started = true;
        }

        if (started)
        {
            FrontEndDrivenLog.Log("surface.light.pump_started", new
            {
                uiThread = _uiThreadId,
                messageLoop = Application.MessageLoop,
                intervalMs = IntervalMs,
            });
        }
    }

    /// <summary>屏幕覆盖区（屏幕 DIP）；与 <See cref="Pull"/> 的换算同一口径，供无帧回落帧使用。</summary>
    internal static SurfaceLightCoverage ScreenCoverage(Control anchor)
    {
        var screen = SystemInformation.VirtualScreen;
        var dpi = BorderUnits.NormalizeDpi(anchor.DeviceDpi > 0
            ? anchor.DeviceDpi
            : (_pixelScaleDpi > 0 ? _pixelScaleDpi : 96));
        var loc = SurfaceLocMath.ScreenCoverageOf(
            screen.X, screen.Y, screen.Width, screen.Height, BorderUnits.BaseDpi / dpi);
        return new SurfaceLightCoverage(loc.X, loc.Y, loc.Width, loc.Height);
    }

    private static void Pull()
    {
        System.Threading.Interlocked.Increment(ref _ticks);
        if (_pullInProgress) return;
        _pullInProgress = true;
        try
        {
            if (RegistryCoordinator.Registry is not ITrConfigRegistry registry)
            {
                SetReason("registry-missing", "RegistryCoordinator.Registry 不是配置注册表");
                DegradeIfIdle();
                return;
            }

            if (!registry.TryGetDelegate("Engine_SampleSurfaceLight", out var function)
                || function is not Func<int, int, int, int, string, string> sample)
            {
                // 原因码 port-missing：引擎函数未注册（宿主未加载引擎）⇒ 明确判定，而不是静默 null。
                SetReason("port-missing", "Engine_SampleSurfaceLight 未注册或签名不符");
                DegradeIfIdle();
                return;
            }

            _idleTicks = 0;
            // 覆盖区唯一换算入口（ScreenCoverage）；像素矩形由同一 DIP 结果取整，避免两套 DPI/scale 计算漂移。
            var anchor = _anchor;
            if (anchor is null || anchor.IsDisposed)
            {
                SetReason("anchor-missing", "光泵锚点窗口已释放");
                DegradeIfIdle();
                return;
            }

            var coverage = ScreenCoverage(anchor);
            var rect = new SurfaceScreenRectLoc(
                (int)Math.Round(coverage.X), (int)Math.Round(coverage.Y),
                Math.Max(1, (int)Math.Round(coverage.Width)), Math.Max(1, (int)Math.Round(coverage.Height)));

            var raw = sample(rect.X, rect.Y, rect.Width, rect.Height, _look.ToJson());
            var frame = SurfaceLightFrameCodec.Parse(raw);
            if (frame is null)
            {
                // 原因码 port-parse-null：端口已注册且被调用，但快照被引擎明示不可用（diag）或格式不符。
                // 与端口异常同一退化节奏：先保留上一帧，连续失败达阈值才发布 null（避免单次抖动闪断光源）。
                SetReason("port-parse-null", Clip(raw, 160));
                SkinDiagnostics.RecordLightDegraded("port-parse-null", Clip(raw, 160));
                DegradeIfIdle();
                return;
            }

            SurfaceLightRegistry.Publish(frame);
            var published = System.Threading.Interlocked.Increment(ref _published);
            System.Threading.Interlocked.Exchange(ref _lastPublishTick, Environment.TickCount64);
            SetReason("publishing", "frame");
            if (published == 1)
            {
                FrontEndDrivenLog.Log("surface.light.pump_published", new
                {
                    coverage = new { rect.X, rect.Y, rect.Width, rect.Height },
                    cells = frame.Cells.Length,
                    uiThread = _uiThreadId,
                });
            }
        }
        catch (Exception ex)
        {
            // 原因码 port-exception：端口调用本身抛出（跨 ALC 类型/序列化等）。
            SetReason("port-exception", ex.GetType().Name + ":" + ex.Message);
            SkinDiagnostics.RecordLightDegraded("port-exception", ex.GetType().Name + ":" + ex.Message);
            // 通道异常只能降级为「无帧」，不得让绘制线程承担光计算。
            DegradeIfIdle();
        }
        finally
        {
            _pullInProgress = false;
        }
    }

    /// <summary>测试/诊断用：手工推进一拍（与 Timer.Tick 同一路径）。</summary>
    internal static void PullOnce() => Pull();

    /// <summary>记录最近判定原因；仅原因变化时写一条日志，避免 1s 节拍刷屏。</summary>
    private static void SetReason(string reason, string detail)
    {
        var changed = false;
        lock (Gate)
        {
            if (!string.Equals(_lastReason, reason, StringComparison.Ordinal))
            {
                _lastReason = reason;
                changed = true;
            }
        }
        if (changed)
        {
            FrontEndDrivenLog.Log("surface.light.pump_reason", new { reason, detail });
        }
    }

    /// <summary>无帧（端口缺失/调用抛错）退化：连续 <see cref="RetryTicks"/> 次后发布 null（载体保持声明值）。</summary>
    private static void DegradeIfIdle()
    {
        _idleTicks++;
        if (_idleTicks < RetryTicks) return;
        _idleTicks = 0;
        SurfaceLightRegistry.Publish(null);
        var reason = _lastReason;
        FrontEndDrivenLog.Log("surface.light.pump_null", new { reason, ticks = RetryTicks });
    }

    /// <summary>诊断用：截断引擎快照，避免一次性日志被整帧 cells 撑爆。</summary>
    private static string Clip(string? value, int limit)
    {
        if (value is null) return "<null>";
        return value.Length <= limit ? value : value.Substring(0, limit) + "...";
    }
}
