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

    /// <summary>尚未挂进窗口/句柄未就绪的待挂控件（弱引用；空闲时重试一次即可解析）。</summary>
    private static readonly List<WeakReference<Control>> Pending = new();

    private static readonly ConditionalWeakTable<Control, object> Deferred = new();
    private static readonly object DeferredMarker = new();

    private static EventHandler? _idleHandler;
    private static long _ensureAttempts;
    private static long _ensureFailures;
    private static string _lastEnsureReason = "not-called";

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
    private bool _attachLogged;

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

    /// <summary>确保帧宿主启动的尝试次数（诊断；含延迟挂起）。</summary>
    internal static long EnsureAttemptCount => System.Threading.Interlocked.Read(ref _ensureAttempts);

    /// <summary>帧宿主启动失败次数（诊断；静默跳过已消除）。</summary>
    internal static long EnsureFailureCount => System.Threading.Interlocked.Read(ref _ensureFailures);

    /// <summary>最近一次宿主启动判定（ready / deferred:&lt;原因&gt; / &lt;失败原因&gt;）。</summary>
    internal static string LastEnsureReason => System.Threading.Volatile.Read(ref _lastEnsureReason);

    /// <summary>仍待窗口就绪的控件数（诊断）。</summary>
    internal static int PendingEnsureCount
    {
        get { lock (Pending) return Pending.Count; }
    }

    /// <summary>
    /// 通用帧宿主入口：控件尚未挂进窗口/句柄未就绪 ⇒ 登记延迟重试并记录原因，
    /// 不再静默空转后被永久掐断。
    /// </summary>
    internal static void EnsureFor(Control? control)
    {
        if (control is null || control.IsDisposed) return;
        System.Threading.Interlocked.Increment(ref _ensureAttempts);
        if (TryEnsureFor(control, out var reason))
        {
            ResolveDeferred(control);
            return;
        }
        Defer(control, reason);
    }

    private static bool TryEnsureFor(Control control, out string reason)
    {
        if (control.IsDisposed)
        {
            reason = "disposed";
            return false;
        }
        if (!control.IsHandleCreated)
        {
            reason = "handle-missing";
            return false;
        }
        if (control.InvokeRequired)
        {
            reason = "off-ui-thread";
            return false;
        }
        var form = control.FindForm();
        if (form is null)
        {
            reason = "form-missing";
            return false;
        }
        Ensure(form);
        MarkDirty(control, "theme");
        reason = string.Empty;
        return true;
    }

    private static void Defer(Control control, string reason)
    {
        if (!Deferred.TryGetValue(control, out _))
        {
            Deferred.Add(control, DeferredMarker);
            control.ParentChanged += OnDeferredControlChanged;
            control.HandleCreated += OnDeferredControlChanged;
            control.Disposed += OnDeferredControlDisposed;
            lock (Pending) Pending.Add(new WeakReference<Control>(control));
            FrontEndDrivenLog.Log("surface.frame.host_deferred", new
            {
                window = SurfaceAmbient.IdOf(control),
                type = control.GetType().Name,
                name = control.Name,
                reason,
            });
        }
        Volatile.Write(ref _lastEnsureReason, "deferred:" + reason);

        if (!control.InvokeRequired)
        {
            // 与控件同线程（通常是 UI 线程）：空闲时重试，覆盖「祖先稍后挂进窗口」这类叶子控件无事件的情形。
            EnsureIdleHook();
            return;
        }

        // 已在句柄所属线程之外：投递一次到 UI 线程重试（UI 线程尚无消息循环时会抛，交由控件事件兜底）。
        if (control.IsHandleCreated)
        {
            try
            {
                control.BeginInvoke((Action)(() => EnsureFor(control)));
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private static void OnDeferredControlChanged(object? sender, EventArgs e)
    {
        if (sender is Control control) EnsureFor(control);
    }

    private static void OnDeferredControlDisposed(object? sender, EventArgs e)
    {
        if (sender is Control control) ResolveDeferred(control);
    }

    private static void ResolveDeferred(Control control)
    {
        if (!Deferred.Remove(control)) return;
        control.ParentChanged -= OnDeferredControlChanged;
        control.HandleCreated -= OnDeferredControlChanged;
        control.Disposed -= OnDeferredControlDisposed;
        lock (Pending)
        {
            for (var i = Pending.Count - 1; i >= 0; i--)
            {
                if (!Pending[i].TryGetTarget(out var target) || ReferenceEquals(target, control))
                    Pending.RemoveAt(i);
            }
        }
        if (PendingEnsureCount == 0) UnhookIdle();
    }

    private static void EnsureIdleHook()
    {
        if (_idleHandler is not null) return;
        _idleHandler = OnIdle;
        Application.Idle += _idleHandler;
    }

    private static void UnhookIdle()
    {
        if (_idleHandler is null) return;
        Application.Idle -= _idleHandler;
        _idleHandler = null;
    }

    private static void OnIdle(object? sender, EventArgs e)
    {
        Control[] snapshot;
        lock (Pending)
        {
            if (Pending.Count == 0)
            {
                UnhookIdle();
                return;
            }
            var live = new List<Control>(Pending.Count);
            for (var i = Pending.Count - 1; i >= 0; i--)
            {
                if (Pending[i].TryGetTarget(out var target)) live.Add(target);
                else Pending.RemoveAt(i);
            }
            snapshot = live.ToArray();
        }

        var remaining = false;
        foreach (var control in snapshot)
        {
            if (control.IsDisposed || TryEnsureFor(control, out _)) ResolveDeferred(control);
            else remaining = true;
        }
        if (!remaining) UnhookIdle();
    }

    private static void RecordEnsureFailure(string reason, Form? form, Exception? exception)
    {
        System.Threading.Interlocked.Increment(ref _ensureFailures);
        Volatile.Write(ref _lastEnsureReason, reason);
        FrontEndDrivenLog.Log("surface.frame.host_unavailable", new
        {
            window = form is null ? string.Empty : SurfaceAmbient.IdOf(form),
            type = form?.GetType().Name ?? string.Empty,
            reason,
            exception = exception is null ? string.Empty : exception.GetType().Name + ":" + exception.Message,
        });
    }

    /// <summary>确保窗口宿主存在并起表（幂等；UI 线程调用）。失败不静默：记录原因后返回 null。</summary>
    internal static SurfaceRefreshHost? Ensure(Form? form)
    {
        if (form is null || form.IsDisposed)
        {
            RecordEnsureFailure(form is null ? "form-null" : "form-disposed", form, null);
            return null;
        }

        SurfaceLightPump.EnsureStarted(form);
        WinFormsPointerSource.Ensure(form);

        if (!Hosts.TryGetValue(form, out var host))
        {
            try
            {
                host = new SurfaceRefreshHost(form);
                Hosts.Add(form, host);
            }
            catch (Exception ex)
            {
                RecordEnsureFailure("factory-failed", form, ex);
                return null;
            }
        }

        try
        {
            host.Attach();
        }
        catch (Exception ex)
        {
            RecordEnsureFailure("attach-failed", form, ex);
            return null;
        }

        if (!host._attachLogged)
        {
            host._attachLogged = true;
            Volatile.Write(ref _lastEnsureReason, "ready");
            FrontEndDrivenLog.Log("surface.frame.host_ready", new
            {
                window = host._windowId,
                tickMs = 1000,
                uiThread = System.Environment.CurrentManagedThreadId,
                messageLoop = Application.MessageLoop,
            });
        }
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
        // 指针是事件驱动成员：输入源（应用消息过滤器）变化后发布到 registry，
        // 由 PointerRevision 去重，再 MarkDirty("pointer") + 踢一拍（复用 RefreshScheduler 的单飞，不新增第二个 Timer）。
        WinFormsPointerSource.Instance.Changed += OnPointerChanged;
    }

    private long _lastPointerDirtyMs;

    /// <summary>指针事件源：16ms 合并后发布指针成员并踢一拍（无定时轮询）。</summary>
    private void OnPointerChanged()
    {
        var now = System.Environment.TickCount64;
        if (now - _lastPointerDirtyMs < 16) return;
        _lastPointerDirtyMs = now;
        PublishPointer();
    }

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
        WinFormsPointerSource.Instance.Changed -= OnPointerChanged;
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
