using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Strategy.Refresh.Model;
using Strategy.Refresh.Ports;

namespace Strategy.Refresh.Core
{
    /// <summary>
    /// 刷新编排面（L1，**唯一入口**）：三车道调度 + 渲染产物 + 版本/指纹仲裁 + 写落地 + 反馈状态。
    /// <para>页面 Pull / 宿主 Push / 手动刷新统一经 <see cref="ExecuteAsync"/>；不再暴露 RenderAsync / CommitAsync 等旁路入口。</para>
    /// </summary>
    public sealed class RefreshService : IDisposable
    {
        /// <summary>通道推送出口（宿主注入）：scopes=null 表示全量；返回 false = 未送达。</summary>
        public delegate Task<bool> ChannelPush(IReadOnlyCollection<string>? scopes, CancellationToken ct);

        private readonly RefreshScheduler _scheduler;
        private readonly RefreshArbiter _arbiter;
        private readonly IRefreshStore _store;
        private readonly IFingerprint _fingerprint;
        private readonly RefreshSchedulerOptions _options;
        private readonly RefreshBeatOwnership _beats = new();
        private readonly Action<string, object?>? _diagnostics;
        private long _unchangedDeliveries;

        public RefreshService(
            ChannelPush push,
            IRefreshSink sink,
            IInFlightSource? inFlightSource = null,
            IRefreshStore? store = null,
            IFingerprint? fingerprint = null,
            IStallPolicy? stallPolicy = null,
            RefreshSchedulerOptions? options = null,
            IRefreshClock? clock = null,
            Action<string, object?>? diagnostics = null)
        {
            ArgumentNullException.ThrowIfNull(push);
            ArgumentNullException.ThrowIfNull(sink);

            _diagnostics = diagnostics;
            _options = options ?? new RefreshSchedulerOptions();
            _fingerprint = fingerprint ?? Fnv1aFingerprint.Default;
            _store = store ?? new InMemoryRefreshStore(_options);
            _arbiter = new RefreshArbiter(_store, sink);
            _scheduler = new RefreshScheduler(
                _options,
                (scopes, token) => push(scopes, token),
                inFlightSource,
                _store,
                stallPolicy,
                onDiagnostics: null,
                timerFactory: ToTimerFactory(clock));
        }

        public RefreshRegistry Registry { get; } = new();

        public IRefreshStore Store => _store;

        public IFingerprint Fingerprint => _fingerprint;

        public bool IsRunning => _scheduler.IsRunning;

        public long PushCount => _scheduler.PushCount;

        public long UnchangedDeliveries => Interlocked.Read(ref _unchangedDeliveries);

        /// <summary>被「一拍一票」拦下的重复拍数（同一 slot + 同一 traceId 的第二次请求；已在渲染前早退）。</summary>
        public long SuppressedDuplicateBeats => _beats.SuppressedCount;

        public long SuppressedHeartbeats => _scheduler.SuppressedHeartbeats;

        public bool RegisterSource(RefreshDescriptor descriptor, IRefreshSource source)
        {
            var added = Registry.RegisterSource(descriptor, source);
            if (added && IsRunning) _scheduler.RegisterSource(source);
            return added;
        }

        public bool UnregisterSource(IRefreshSource source)
        {
            _scheduler.UnregisterSource(source);
            return Registry.UnregisterSource(source);
        }

        public bool RegisterRenderer(IRefreshRenderer renderer) => Registry.RegisterRenderer(renderer);

        /// <summary>绑定：起表 + 启动已登记源。</summary>
        public void Attach()
        {
            foreach (var source in Registry.Sources()) _scheduler.RegisterSource(source);
            _scheduler.Start();
        }

        public void Detach() => _scheduler.Stop();

        public void Start() => _scheduler.Start();

        public void Stop() => _scheduler.Stop();

        public void EnableIdlePolling(int intervalMs = RefreshContract.FallbackPollMs, string? scopeKey = null)
            => _scheduler.EnableIdlePolling(intervalMs, scopeKey);

        public void MarkDirty() => _scheduler.MarkDirty();

        public void MarkDirty(string? scopeKey) => _scheduler.MarkDirty(scopeKey);

        public void MarkDirty(string? scopeKey, RefreshPriority priority) => _scheduler.MarkDirty(scopeKey, priority);

        public Task TickAsync() => _scheduler.TickAsync();

        public Task<bool> PushOnceAsync(CancellationToken ct = default) => _scheduler.PushOnceAsync(ct);

        /// <summary>
        /// 执行一次完整刷新周期：渲染产物 → 版本/指纹裁决 → 单飞交付 → 推进/记录反馈状态。
        /// <para>异常 / 空指纹 / 渲染失败一律收敛为 <see cref="RefreshOutcome.Failed"/>，不向调用方抛出。</para>
        /// </summary>
        public async Task<RefreshCycleResult> ExecuteAsync(
            RefreshRequest request,
            RefreshTrigger trigger,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            var started = _options.UtcNow();
            var slot = request.SlotKey;

            // 一拍一票（唯一拍所有权）：页面回投的同一 (slot, traceId) 只允许渲染一次。
            // 重复投递（消息重入 / 页面重复回投）⇒ 渲染前早退为 Unchanged，不产生第二次全量重投影。
            // 仅约束 PagePull：Change/Manual/Heartbeat 各有独立驱动语义，不得因拍号复用被吞。
            if (trigger == RefreshTrigger.PagePull && !_beats.TryBegin(slot, request.TraceId))
            {
                var beatState = _store.GetState(slot);
                // P0-3：早退必须留轨迹（否则「一次刷新零写回」不可归因）。
                _diagnostics?.Invoke("刷新轨迹", new
                {
                    slot, trigger = trigger.ToString(), reason = "beat-suppressed",
                    version = beatState.LastDeliveredVersion,
                });
                return Result(slot, beatState.LastDeliveredVersion, trigger, RefreshOutcome.Unchanged,
                    AcceptDecision.Skip, false, false, false, beatState.LastFingerprint, started, null, null);
            }

            // 版本单源早退（渲染前）：页面回投的 baseVersion 与本槽已交付版本相同 ⇒ 页面已持有该内容，
            // 本拍只做「确认」不做重投影（消掉每拍必然 NextVersion ⇒ 必然 RenderAsync 的白跑）。
            if (request.BaseVersion is long baseVersion)
            {
                var versionState = _store.GetState(slot);
                if (baseVersion == versionState.LastDeliveredVersion)
                {
                    _store.RecordOutcome(slot, RefreshOutcome.Unchanged, versionState.LastFingerprint, null, started);
                    // P0-3：版本相同早退同样留轨迹（reason 受控三值之一）。
                    _diagnostics?.Invoke("刷新轨迹", new
                    {
                        slot, trigger = trigger.ToString(), reason = "version-same",
                        version = versionState.LastDeliveredVersion,
                    });
                    return Result(slot, versionState.LastDeliveredVersion, trigger, RefreshOutcome.Unchanged,
                        AcceptDecision.Skip, false, false, false, versionState.LastFingerprint, started, null, null);
                }
            }

            if (trigger is RefreshTrigger.Change or RefreshTrigger.Manual)
                _store.ResetProgress(slot, started);

            long version;
            try
            {
                version = _store.NextVersion(slot);
            }
            catch (Exception ex)
            {
                return Result(slot, 0, trigger, RefreshOutcome.Failed, AcceptDecision.Apply,
                    false, false, false, string.Empty, started, null, ex);
            }

            RefreshArtifact artifact;
            try
            {
                artifact = await Registry.Renderer(slot).RenderAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _store.RecordOutcome(slot, RefreshOutcome.Failed, string.Empty, null, _options.UtcNow());
                return Result(slot, version, trigger, RefreshOutcome.Failed, AcceptDecision.Apply,
                    false, false, false, string.Empty, started, null, ex);
            }

            if (artifact is null)
            {
                _store.RecordOutcome(slot, RefreshOutcome.Failed, string.Empty, null, _options.UtcNow());
                return Result(slot, version, trigger, RefreshOutcome.Failed, AcceptDecision.Apply,
                    false, false, false, string.Empty, started, null,
                    new InvalidOperationException("渲染器返回 null 产物"));
            }

            var fingerprint = artifact.Fingerprint ?? string.Empty;
            if (fingerprint.Length == 0)
            {
                _store.RecordOutcome(slot, RefreshOutcome.Failed, string.Empty, artifact.ProgressToken, _options.UtcNow());
                return Result(slot, version, trigger, RefreshOutcome.Failed, AcceptDecision.Apply,
                    false, false, false, string.Empty, started, null,
                    new InvalidOperationException("渲染产物指纹为空（按 Failed 处理）"));
            }

            if (artifact.Outcome != RefreshOutcome.Changed || artifact.Payload is null)
            {
                _store.RecordOutcome(slot, artifact.Outcome, fingerprint, artifact.ProgressToken, _options.UtcNow());
                return Result(slot, version, trigger, artifact.Outcome, AcceptDecision.Apply,
                    false, false, false, fingerprint, started, artifact.Payload, null);
            }

            RefreshArbitrationResult arbitration;
            try
            {
                arbitration = await _arbiter.CommitAsync(
                    new RefreshTicket(slot, version, request.TraceId),
                    fingerprint,
                    () => artifact.Payload!,
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _store.RecordOutcome(slot, RefreshOutcome.Failed, fingerprint, artifact.ProgressToken, _options.UtcNow());
                return Result(slot, version, trigger, RefreshOutcome.Failed, AcceptDecision.Apply,
                    false, false, false, fingerprint, started, artifact.Payload, ex);
            }

            var outcome = MapOutcome(arbitration);
            if (arbitration.Delivery.Delivered && !arbitration.Delivery.ContentChanged)
                Interlocked.Increment(ref _unchangedDeliveries);
            if (!arbitration.Merged)
                _store.RecordOutcome(slot, outcome, fingerprint, artifact.ProgressToken, _options.UtcNow());

            return Result(slot, version, trigger, outcome, arbitration.Decision,
                arbitration.Delivery.Delivered, arbitration.Delivery.ContentChanged, arbitration.Merged,
                fingerprint, started, artifact.Payload, arbitration.Delivery.Error);
        }

        private static RefreshOutcome MapOutcome(RefreshArbitrationResult arbitration)
        {
            if (arbitration.Decision == AcceptDecision.Stale) return RefreshOutcome.Skipped;
            if (arbitration.Decision == AcceptDecision.Skip) return RefreshOutcome.Unchanged;
            if (!arbitration.Delivery.Delivered) return RefreshOutcome.Failed;
            return arbitration.Delivery.ContentChanged ? RefreshOutcome.Changed : RefreshOutcome.Unchanged;
        }

        private RefreshCycleResult Result(
            string slot,
            long version,
            RefreshTrigger trigger,
            RefreshOutcome outcome,
            AcceptDecision decision,
            bool delivered,
            bool changed,
            bool merged,
            string fingerprint,
            DateTimeOffset started,
            object? payload,
            Exception? error)
            => new(
                slot, version, trigger, outcome, decision, delivered, changed, merged,
                fingerprint, _options.UtcNow() - started, payload, error);

        private static Func<TimeSpan, TimerCallback, IDisposable>? ToTimerFactory(IRefreshClock? clock)
            => clock is null
                ? null
                : (interval, callback) => clock.Schedule(interval, () => callback(null));

        public void Dispose()
        {
            foreach (var source in Registry.Sources()) _scheduler.UnregisterSource(source);
            _scheduler.Dispose();
        }
    }
}


