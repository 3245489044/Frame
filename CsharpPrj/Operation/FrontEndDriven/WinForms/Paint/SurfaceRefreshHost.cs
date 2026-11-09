#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Share.Composition.AssemblyFramework;
using Share.Conversation.Rendering.Surface;
using Share.SurfacePaint.Hosting;
using SurfacePaint.Contracts;
using Strategy.Refresh.Core;
using Strategy.Refresh.Model;
using Strategy.Refresh.Ports;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 窗口级 Surface 刷新宿主（**唯一接线点**）：把 Strategy.Refresh 的调度/退避/单飞
/// 接到窗口帧生产上。<see cref="SkinDecorator"/> 不再为每个控件建 Timer。
/// </summary>
internal sealed class SurfaceRefreshHost
{
    private static readonly ConditionalWeakTable<Form, SurfaceRefreshHost> Hosts = new();

    private readonly Form _form;
    private readonly string _windowId;
    private readonly string _slotKey;
    private readonly SurfaceFrameStore _store = new();
    private readonly SurfaceFrameRefreshRenderer _renderer;
    private readonly SurfaceFrameRefreshSink _sink;
    private readonly SurfaceLightRefreshSource _lightSource = new();
    private readonly SurfaceLayoutRefreshSource _layoutSource;
    private RefreshService? _service;
    private string? _deliveredFingerprint;
    private bool _pointerSeeded;

    private static long _pushes;
    private static long _unchanged;
    private static long _pointerPublishes;
    private static long _pointerKicks;

    /// <summary>窗口帧推送次数（诊断；框架 UnchangedDeliveries 恒 0，故自建）。</summary>
    internal static long PushCount => System.Threading.Interlocked.Read(ref _pushes);

    /// <summary>未变化帧的交付次数（诊断；自建计数）。</summary>
    internal static long UnchangedDeliveries => System.Threading.Interlocked.Read(ref _unchanged);

    /// <summary>指针成员发布次数（A2 诊断：静止时不再增长）。</summary>
    internal static long PointerPublishCount => System.Threading.Interlocked.Read(ref _pointerPublishes);

    /// <summary>事件驱动踢拍次数（A2 诊断：静止后不再增长）。</summary>
    internal static long PointerKickCount => System.Threading.Interlocked.Read(ref _pointerKicks);

    private SurfaceRefreshHost(Form form)
    {
        _form = form;
        _windowId = SurfaceAmbient.IdOf(form);
        _slotKey = "surface:" + _windowId;
        _sink = new SurfaceFrameRefreshSink(Deliver);
        _renderer = new SurfaceFrameRefreshRenderer(
            _slotKey,
            CaptureInput,
            new SurfaceFrameComposer(
                WinFormsSurfacePorts.Engine ?? WinFormsSurfaceEnginePort.Instance,
                WinFormsSurfacePorts.Log),
            SkinDecorator.FrameGatherSource,
            SkinDecorator.FrameModuleSource,
            WinFormsPointerSource.Instance);
        _layoutSource = new SurfaceLayoutRefreshSource(form);
        _form.FormClosed += (_, _) => Detach();
    }

    /// <summary>确保窗口宿主存在并起表（幂等；UI 线程调用）。</summary>
    internal static SurfaceRefreshHost? Ensure(Form? form)
    {
        if (form is null) return null;
        SurfaceLightPump.EnsureStarted(form);
        WinFormsPointerSource.Ensure(form);
        var host = Hosts.GetValue(form, static value => new SurfaceRefreshHost(value));
        host.Attach();
        return host;
    }

    /// <summary>取窗口最新快照（未生产 ⇒ <see cref="SurfaceFrameSnapshot.Empty"/>）。</summary>
    internal static SurfaceFrameSnapshot SnapshotOf(Form? form)
    {
        if (form is null) return SurfaceFrameSnapshot.Empty;
        return Hosts.TryGetValue(form, out var host)
            ? host._store.Read(host._windowId) ?? SurfaceFrameSnapshot.Empty
            : SurfaceFrameSnapshot.Empty;
    }

    /// <summary>标记窗口脏（供载体事件/主题切换调用）。</summary>
    internal static void MarkDirty(Control? control, string scope)
    {
        var form = control?.FindForm();
        if (form is null) return;
        if (Hosts.TryGetValue(form, out var host)) host._service?.MarkDirty(scope);
    }

    /// <summary>绘制完成后释放更早代（幂等；借用位图仍归帧泵所有）。</summary>
    internal static void ReleaseConsumed(Control? control, SurfaceFrameSnapshot? snapshot)
    {
        if (snapshot is null) return;
        var form = control?.FindForm();
        if (form is null || !Hosts.TryGetValue(form, out var host)) return;
        host._store.Release(snapshot.WindowId, snapshot.Generation);
    }

    private void Attach()
    {
        if (_service is not null) return;

        // 背景事实由真实 producer（SkinDecorator → SurfaceWindowBackdropSource）在采集后写入；
        // 这里不再登记占位（Content=null）以免冒充可用背景。
        // TickMs 是「脏集合并发节拍」，不是周期重绘源：无脏集/无在飞源时 tick 不产帧（空闲零重绘）。
        // 0/负值会被 RefreshScheduler 拒绝（见 RefreshContract），故保留 1000ms 作为合并上限。
        var service = new RefreshService(
            PushOnceAsync,
            _sink,
            options: new RefreshSchedulerOptions { TickMs = 1000 },
            diagnostics: (name, data) => FrontEndDriven.Diagnostics.FrontEndDrivenLog.Log(
                "surface.refresh", new { name, data }));
        service.RegisterRenderer(_renderer);
        service.RegisterSource(new RefreshDescriptor("surface-light", "surface-light"), _lightSource);
        service.RegisterSource(new RefreshDescriptor("surface-layout", "surface-layout"), _layoutSource);
        service.EnableIdlePolling(30_000, "layout");
        service.Attach();
        _service = service;
        // 指针是事件驱动成员：发布到 registry 后由 PointerChanged → MarkDirty("pointer")，
        // 这里再踢一拍（复用 RefreshScheduler.TickAsync 的单飞，不新增第二个 Timer）。
        _form.MouseMove += OnPointerMove;
        _form.MouseLeave += OnPointerLeave;
    }

    private long _lastPointerDirtyMs;

    /// <summary>指针事件源：16ms 合并后发布指针成员并踢一拍（无定时轮询）。</summary>
    private void OnPointerMove(object? sender, MouseEventArgs e)
    {
        var now = System.Environment.TickCount64;
        if (now - _lastPointerDirtyMs < 16) return;
        _lastPointerDirtyMs = now;
        PublishPointer();
    }

    /// <summary>离开窗口 ⇒ 清空指针成员（唯一一次，registry 去重后不再通知）。</summary>
    private void OnPointerLeave(object? sender, EventArgs e) => PublishPointer();

    /// <summary>
    /// 发布指针成员（屏幕 DIP）+ 踢一拍。静止时 registry 以 &lt;0.5 DIP 去重，
    /// 因此不会产生新的 MarkDirty / 踢拍 / 重绘（A2 空闲零重绘）。
    /// </summary>
    private void PublishPointer()
    {
        if (_service is null || _form.IsDisposed || !_form.IsHandleCreated) return;
        PublishPointer(WinFormsPointerSource.Instance.Read());
    }

    /// <summary>把一份指针状态发布为光源成员，并踢一拍（事件驱动，无第二定时器）。</summary>
    private void PublishPointer(SurfacePointerState pointer)
    {
        var service = _service;
        if (service is null || _form.IsDisposed || !_form.IsHandleCreated) return;

        var dpi = _form.DeviceDpi > 0 ? _form.DeviceDpi : 96;
        var origin = _form.PointToScreen(Point.Empty);
        var originX = origin.X * 96.0 / dpi;
        var originY = origin.Y * 96.0 / dpi;
        var widthDip = _form.ClientSize.Width * 96.0 / dpi;
        var heightDip = _form.ClientSize.Height * 96.0 / dpi;

        var emitter = SurfaceLightPointer.BuildPointerEmitter(pointer, originX, originY, widthDip, heightDip);
        var before = SurfaceLightRegistry.PointerRevision;
        SurfaceLightRegistry.PublishPointer(emitter);
        if (SurfaceLightRegistry.PointerRevision == before) return; // 静止：不标脏、不踢拍。

        System.Threading.Interlocked.Increment(ref _pointerPublishes);
        service.MarkDirty("pointer");
        Kick(service);
    }

    private int _kickQueued;
    private long _kickRequested;
    private long _kickServed;

    /// <summary>事件驱动踢拍：单飞 + 尾随合并（TickAsync 本身不防重入，这里负责）。</summary>
    private void Kick(RefreshService service)
    {
        System.Threading.Interlocked.Increment(ref _kickRequested);
        if (System.Threading.Interlocked.CompareExchange(ref _kickQueued, 1, 0) != 0) return;
        _ = KickAsync(service);
    }

    private async Task KickAsync(RefreshService service)
    {
        try
        {
            while (true)
            {
                var target = System.Threading.Interlocked.Read(ref _kickRequested);
                if (target == System.Threading.Interlocked.Read(ref _kickServed)) return;
                await service.TickAsync().ConfigureAwait(false);
                System.Threading.Interlocked.Exchange(ref _kickServed, target);
                System.Threading.Interlocked.Increment(ref _pointerKicks);
                if (System.Threading.Interlocked.Read(ref _kickRequested) == target) return;
            }
        }
        catch (ObjectDisposedException)
        {
            // 窗口关闭/服务释放：静默停止。
        }
        catch (Exception ex)
        {
            FrontEndDriven.Diagnostics.FrontEndDrivenLog.Log(
                "surface.refresh", new { name = "pointer-kick", error = ex.GetType().Name });
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _kickQueued, 0);
            // 退出瞬间若又有新请求，补一次（避免最后一次指针位置等到 1s 表拍）。
            if (System.Threading.Interlocked.Read(ref _kickRequested) != System.Threading.Interlocked.Read(ref _kickServed) &&
                System.Threading.Interlocked.CompareExchange(ref _kickQueued, 1, 0) == 0)
            {
                _ = KickAsync(service);
            }
        }
    }

    private async Task<bool> PushOnceAsync(IReadOnlyCollection<string>? scopes, CancellationToken ct)
    {
        if (_service is null) return false;
        var request = new RefreshRequest(_slotKey, scopes?.ToArray());
        var result = await _service.ExecuteAsync(request, RefreshTrigger.Change, ct).ConfigureAwait(false);
        System.Threading.Interlocked.Increment(ref _pushes);
        if (result.Outcome == RefreshOutcome.Unchanged)
        {
            System.Threading.Interlocked.Increment(ref _unchanged);
        }

        return result.Outcome != RefreshOutcome.Failed;
    }

    private void Detach()
    {
        _lightSource.Stop();
        _layoutSource.Stop();
        _form.MouseMove -= OnPointerMove;
        _form.MouseLeave -= OnPointerLeave;
        SurfaceLightRegistry.PublishPointer(null);
        _service?.Detach();
        _store.Clear();
        _service = null;
    }

    private SurfaceFrameInput? CaptureInput()
    {
        if (_form.IsDisposed || _form.Disposing) return null;
        SurfaceFrameInput? captured = null;
        void Capture() => captured = SkinDecorator.CaptureWindow(_form);

        if (_form.InvokeRequired)
        {
            _form.Invoke((Action)Capture);
        }
        else
        {
            Capture();
        }

        return captured;
    }

    private void Deliver(SurfaceFrameSnapshot snapshot)
    {
        // 快照里的指针是帧级真值的兜底：首帧（事件尚未到达）用它把成员补进 registry，避免死字段。
        if (!_pointerSeeded && snapshot.Pointer.Inside && SurfaceLightRegistry.Pointer is null)
        {
            _pointerSeeded = true;
            var pointer = snapshot.Pointer;
            if (_form.InvokeRequired)
            {
                _form.BeginInvoke(new Action(() => PublishPointer(pointer)));
            }
            else
            {
                PublishPointer(pointer);
            }
        }

        _store.Publish(snapshot);
        if (_form.IsDisposed || !_form.IsHandleCreated) return;

        // 结构与上一帧完全相同 ⇒ 不失效（空闲零重绘；渲染器已去重，这里是最后一道门）。
        if (string.Equals(snapshot.Fingerprint, _deliveredFingerprint, StringComparison.Ordinal)) return;
        _deliveredFingerprint = snapshot.Fingerprint;

        var keys = snapshot.Cards.Keys.ToArray();
        _form.BeginInvoke(new Action(() =>
        {
            if (_form.IsDisposed) return;
            // 只重绘窗体自身 + 帧内卡片，不失效整窗子树。
            _form.Invalidate(invalidateChildren: false);
            SkinDecorator.InvalidateCards(_form, keys);
        }));
    }
}
