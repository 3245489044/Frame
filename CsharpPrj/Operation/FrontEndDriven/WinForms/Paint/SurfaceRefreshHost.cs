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

    private static long _pushes;
    private static long _unchanged;

    /// <summary>窗口帧推送次数（诊断；框架 UnchangedDeliveries 恒 0，故自建）。</summary>
    internal static long PushCount => System.Threading.Interlocked.Read(ref _pushes);

    /// <summary>未变化帧的交付次数（诊断；自建计数）。</summary>
    internal static long UnchangedDeliveries => System.Threading.Interlocked.Read(ref _unchanged);

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
            SkinDecorator.FrameGatherSource);
        _layoutSource = new SurfaceLayoutRefreshSource(form);
        _form.FormClosed += (_, _) => Detach();
    }

    /// <summary>确保窗口宿主存在并起表（幂等；UI 线程调用）。</summary>
    internal static SurfaceRefreshHost? Ensure(Form? form)
    {
        if (form is null) return null;
        SurfaceLightPump.EnsureStarted(form);
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
