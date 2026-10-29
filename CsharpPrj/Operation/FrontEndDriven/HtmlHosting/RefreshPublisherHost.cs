#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Strategy.Refresh.Core;
using Strategy.Refresh.Diagnostics;
using Strategy.Refresh.Model;
using Strategy.Refresh.Ports;
using UiBackend.Abstractions;
using UiBackend.Html;

namespace FrontEndDriven.HtmlHosting;

/// <summary>
/// 页面刷新发布器宿主（通用实现）—— 组合「刷新服务生命周期 + 推送通道 + 直接写已落地回环消除」，
/// 领域只注册变化源 / 渲染器与注入通道解析。
/// <para>
/// 关键语义：上一次推送之后若直接写已落地，则不再发 update 触发全量重投影（心跳 scopes=null 不受影响）。
/// </para>
/// </summary>
public sealed class RefreshPublisherHost
{
    private readonly object _gate = new();
    private readonly Func<IHtmlChannel?> _channelResolver;
    private readonly Func<string, string>? _traceFactory;
    private readonly IRefreshStore _store;
    private readonly IFingerprint _fingerprint;
    private readonly RefreshSchedulerOptions _options;
    private readonly IInFlightSource? _inFlight;
    private readonly IRefreshSink? _sink;
    private readonly Func<long>? _beatVersion;
    private readonly Action<string, object?>? _diagnostics;
    private RefreshService? _service;
    private HtmlUpdatePusher? _pusher;
    private long _deliveredSeq;
    private long _pushedSeq;

    /// <param name="channelResolver">页面通道解析（返回 null = 未挂载 → 零推送）。</param>
    /// <param name="traceFactory">追踪号工厂（缺省刷新框架 <see cref="RefreshTraceId.New"/>）。</param>
    /// <param name="store">单一刷新状态源（缺省进程内实现）。</param>
    /// <param name="fingerprint">指纹实现（缺省 FNV-1a）。</param>
    /// <param name="options">调度参数（缺省框架默认）。</param>
    /// <param name="inFlight">在途租约源（可选）。</param>
    /// <param name="sink">写落地端口（缺省 <see cref="HtmlRenderOutputSink"/>，带直接写回调）。</param>
    /// <param name="beatVersion">推送携带的内容版本提供者（须为 <see cref="IRefreshStore"/> 已交付版本单源；
    /// 页面据此回投 baseVersion，框架在渲染前早退）。</param>
    /// <param name="diagnostics">刷新轨迹通道（缺省零观测）。早退（beat-suppressed / version-same）与异常必须经此可见化 ——
    /// P0-3：否则「一次刷新零写回」不可归因。</param>
    public RefreshPublisherHost(
        Func<IHtmlChannel?> channelResolver,
        Func<string, string>? traceFactory = null,
        IRefreshStore? store = null,
        IFingerprint? fingerprint = null,
        RefreshSchedulerOptions? options = null,
        IInFlightSource? inFlight = null,
        IRefreshSink? sink = null,
        Func<long>? beatVersion = null,
        Action<string, object?>? diagnostics = null)
    {
        _channelResolver = channelResolver ?? throw new ArgumentNullException(nameof(channelResolver));
        _traceFactory = traceFactory;
        _options = options ?? new RefreshSchedulerOptions();
        _store = store ?? new InMemoryRefreshStore(_options);
        _fingerprint = fingerprint ?? Fnv1aFingerprint.Default;
        _inFlight = inFlight;
        _sink = sink;
        _beatVersion = beatVersion;
        _diagnostics = diagnostics;
    }

    /// <summary>单一刷新状态源（去重/版本裁决）。</summary>
    public IRefreshStore Store => _store;

    /// <summary>指纹实现（领域侧与刷新框架同一口径）。</summary>
    public IFingerprint Fingerprint => _fingerprint;

    /// <summary>刷新编排面（懒创建单实例）。</summary>
    public RefreshService Service
    {
        get
        {
            lock (_gate)
            {
                return _service ??= new RefreshService(
                    PushOnceAsync,
                    _sink ?? new HtmlRenderOutputSink(onDelivered: NotifyDelivered),
                    inFlightSource: _inFlight,
                    store: _store,
                    fingerprint: _fingerprint,
                    options: _options,
                    diagnostics: _diagnostics);
            }
        }
    }

    /// <summary>取已登记渲染器（未装配 / 未登记 ⇒ null）。</summary>
    public IRefreshRenderer? Renderer(string slotKey)
    {
        lock (_gate)
        {
            if (_service is null) return null;
            return _service.Registry.TryGetRenderer(slotKey, out var renderer) ? renderer : null;
        }
    }

    /// <summary>登记变化源（并确保服务已起表）。</summary>
    public void RegisterSource(IRefreshSource source)
    {
        if (source is null) return;
        lock (_gate)
        {
            Service.RegisterSource(new RefreshDescriptor(source.Name, source.Name, Priority: source.Priority), source);
            Service.Attach();
        }
    }

    /// <summary>注销变化源。</summary>
    public void UnregisterSource(IRefreshSource source)
    {
        if (source is null) return;
        lock (_gate) _service?.UnregisterSource(source);
    }

    /// <summary>登记渲染器。</summary>
    public void RegisterRenderer(IRefreshRenderer renderer)
    {
        if (renderer is null) return;
        lock (_gate) Service.RegisterRenderer(renderer);
    }

    /// <summary>标记脏（可选范围）。</summary>
    public void MarkDirty(string? scopeKey = null)
    {
        lock (_gate) _service?.MarkDirty(scopeKey);
    }

    /// <summary>起表 + 启动已登记源。</summary>
    public void Attach()
    {
        lock (_gate) Service.Attach();
    }

    /// <summary>停表（空闲零推送）。</summary>
    public void Detach()
    {
        lock (_gate) _service?.Detach();
    }

    /// <summary>启用空闲轮询。</summary>
    public void EnableIdlePolling(int intervalMs)
    {
        lock (_gate) Service.EnableIdlePolling(intervalMs);
    }

    /// <summary>唯一刷新门：页面 Pull / 宿主 Push / 手动刷新统一经此执行完整周期。</summary>
    public Task<RefreshCycleResult> ExecuteAsync(RefreshRequest request, RefreshTrigger trigger, CancellationToken ct = default)
    {
        RefreshService? service;
        lock (_gate) service = _service;
        if (service is null)
            throw new RefreshNotRegisteredException("刷新编排面未装配：请先注册变化源或调用 Attach");
        return service.ExecuteAsync(request, trigger, ct);
    }

    /// <summary>推送一次刷新请求（宿主 → 页面）：false = 未送达（框架置回重试）。</summary>
    public async Task<bool> PushOnceAsync(IReadOnlyCollection<string>? scopes, CancellationToken ct = default)
    {
        // 刷新回桥短路：上一次推送之后已有「直接写已落地」⇒ 页面 DOM 已更新，不再发 update 触发全量重投影。
        if (scopes is { Count: > 0 })
        {
            long delivered;
            long pushed;
            lock (_gate)
            {
                delivered = _deliveredSeq;
                pushed = _pushedSeq;
                if (delivered > pushed) _pushedSeq = delivered;
            }

            if (delivered > pushed) return true;
        }

        lock (_gate)
            _pusher ??= new HtmlUpdatePusher(_channelResolver, _traceFactory ?? RefreshTraceId.New, _beatVersion);

        var ok = await _pusher.PushAsync(scopes, ct).ConfigureAwait(false);
        if (ok)
        {
            lock (_gate) _pushedSeq = _deliveredSeq;
        }

        return ok;
    }

    /// <summary>直接写落地通知（唯一入口 = 写落地 Sink）：推进「已交付」序号。</summary>
    public void NotifyDelivered()
    {
        lock (_gate) _deliveredSeq++;
    }
}
