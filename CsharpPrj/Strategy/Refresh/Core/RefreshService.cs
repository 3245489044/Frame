using System.Threading;
using System.Threading.Tasks;
using Strategy.Refresh.Diagnostics;
using Strategy.Refresh.Model;
using Strategy.Refresh.Ports;

namespace Strategy.Refresh.Core
{
    /// <summary>
    /// 刷新编排面（L1，**唯一入口**）：把「源事件 → dirty 合并 → 节拍裁决 → 渲染端口 → 写落地 → 版本仲裁」
    /// 收拢为单一实现；各域只登记 <see cref="IRefreshSource"/> / <see cref="IRefreshRenderer"/>。
    /// <para>内含 <see cref="RefreshScheduler"/>（合并 / 节拍 / 单拍互斥 / 失败置回 / 空闲零推送 / 兜底轮询）；
    /// 写落地经 <see cref="IRefreshSink"/>，版本/指纹判定经 <see cref="RefreshArbiter"/> + <see cref="IRefreshStore"/>。</para>
    /// <para>零领域字符串：通道推送、在途判定、渲染全部由上层委托 / 端口注入。</para>
    /// </summary>
    public sealed class RefreshService : IDisposable
    {
        /// <summary>通道推送出口（宿主注入）：scopes=null 表示全量；返回 false = 未送达（调度器置回下一拍重试）。</summary>
        public delegate Task<bool> ChannelPush(IReadOnlyCollection<string>? scopes, CancellationToken ct);

        private readonly RefreshScheduler _scheduler;
        private readonly RefreshArbiter? _arbiter;
        private readonly IFingerprint _fingerprint;
        private readonly IRefreshStore _store;
        private long _pushes;
        private long _pushFailures;

        /// <param name="push">通道推送出口（域侧唯一出口，未挂载时返回 false ⇒ 框架置回重试）。</param>
        /// <param name="hasInFlight">是否存在在途实体（在途期以节拍心跳保底推进度）。</param>
        /// <param name="onResultChanged">推送结果翻转回调（接诊断，避免节拍刷屏）。</param>
        /// <param name="store">单一状态源（null ⇒ 内部 <see cref="InMemoryRefreshStore"/>）。</param>
        /// <param name="fingerprint">指纹单源（null ⇒ <see cref="Fnv1aFingerprint"/>）。</param>
        /// <param name="sink">写落地端口（null ⇒ 不启用仲裁路径 <see cref="CommitAsync"/>）。</param>
        /// <param name="tickMs">节拍（缺省取契约常量 <see cref="RefreshContract.TickMs"/>）。</param>
        /// <param name="clock">定时器工厂端口（缺省真实定时器；测试可注入替身）。</param>
        public RefreshService(
            ChannelPush push,
            Func<bool>? hasInFlight = null,
            Action<bool>? onResultChanged = null,
            IRefreshStore? store = null,
            IFingerprint? fingerprint = null,
            IRefreshSink? sink = null,
            int tickMs = RefreshContract.TickMs,
            IRefreshClock? clock = null)
        {
            ArgumentNullException.ThrowIfNull(push);
            _fingerprint = fingerprint ?? Fnv1aFingerprint.Default;
            _store = store ?? new InMemoryRefreshStore();
            _arbiter = sink is null ? null : new RefreshArbiter(_store, sink);
            var factory = ToTimerFactory(clock);
            _scheduler = new RefreshScheduler(
                tickMs,
                (scopes, ct) => TrackPush(push(scopes, ct)),
                hasInFlight ?? (static () => false),
                onResultChanged,
                factory);
        }

        /// <summary>注册表（源 / 渲染器登记面）。</summary>
        public RefreshRegistry Registry { get; } = new();

        /// <summary>单一状态源（版本水位 + 指纹去重；写仲裁唯一真源）。</summary>
        public IRefreshStore Store => _store;

        /// <summary>指纹单源（与写落地同一口径）。</summary>
        public IFingerprint Fingerprint => _fingerprint;

        /// <summary>节拍是否在役（定时器已建且未停）。</summary>
        public bool IsRunning => _scheduler.IsRunning;

        /// <summary>累计推送次数（诊断 / JSON 门面）。</summary>
        public long PushCount => Interlocked.Read(ref _pushes);

        /// <summary>累计未送达次数（诊断 / JSON 门面）。</summary>
        public long PushFailureCount => Interlocked.Read(ref _pushFailures);

        // ---- 登记 ----

        /// <summary>登记刷新源（幂等；换实例即替换并停旧源）。返回 true = 实际登记。</summary>
        public bool RegisterSource(RefreshDescriptor descriptor, IRefreshSource source)
            => Registry.RegisterSource(descriptor, source);

        /// <summary>登记渲染器（幂等）。返回 true = 新增。</summary>
        public bool RegisterRenderer(IRefreshRenderer renderer) => Registry.RegisterRenderer(renderer);

        /// <summary>注销刷新源（退订 + 停源 + 移出注册表；幂等）。</summary>
        public bool UnregisterSource(IRefreshSource source)
        {
            if (source is null) return false;
            _scheduler.UnregisterSource(source);
            return Registry.UnregisterSource(source);
        }

        // ---- 生命周期 ----

        /// <summary>装配（幂等）：起表 + 启动已登记源。</summary>
        public void Attach()
        {
            foreach (var source in Registry.Sources()) _scheduler.RegisterSource(source);
            _scheduler.Start();
        }

        /// <summary>解绑（幂等）：停表（dirty 保留，重启后可补推，不静默失刷）。</summary>
        public void Detach() => _scheduler.Stop();

        /// <summary>起表（幂等）。</summary>
        public void Start() => _scheduler.Start();

        /// <summary>停表（幂等）。</summary>
        public void Stop() => _scheduler.Stop();

        /// <summary>启用兜底轮询（低优先级生产者，受调度器管辖；幂等）。</summary>
        public void EnableIdlePolling(int intervalMs = RefreshContract.FallbackPollMs, string? scopeKey = null)
            => _scheduler.EnableIdlePolling(intervalMs, scopeKey);

        // ---- 变更标记 ----

        /// <summary>标记「全量有变化待刷新」（高优先级）。</summary>
        public void MarkDirty() => _scheduler.MarkDirty();

        /// <summary>标记「某范围有变化待刷新」（高优先级；空/空白键等价于全量）。</summary>
        public void MarkDirty(string? scopeKey) => _scheduler.MarkDirty(scopeKey);

        /// <summary>按优先级标记变化（低优先不抢占高优先）。</summary>
        public void MarkDirty(string? scopeKey, RefreshPriority priority) => _scheduler.MarkDirty(scopeKey, priority);

        // ---- 推送 / 渲染 / 仲裁 ----

        /// <summary>驱动一拍（高优先车道优先出；空闲零推送）。</summary>
        public Task TickAsync() => _scheduler.TickAsync();

        /// <summary>推送一次刷新请求（全量；返回 false = 未送达）。</summary>
        public Task<bool> PushOnceAsync(CancellationToken ct = default) => _scheduler.PushOnceAsync(ct);

        /// <summary>按槽键渲染一次产物（经登记的 <see cref="IRefreshRenderer"/>；未登记 ⇒ 抛）。</summary>
        public Task<object> RenderAsync(RefreshRequest request, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return Registry.Renderer(request.SlotKey).RenderAsync(request, ct);
        }

        /// <summary>
        /// 渲染并提交仲裁（版本 + 指纹）：Apply 经 sink 落地并推进 baseline；Skip/Stale 不触 DOM。
        /// <para>未注入 sink ⇒ <see cref="RefreshNotRegisteredException"/>（调用边界抛）。</para>
        /// </summary>
        public async Task<AcceptDecision> CommitAsync(
            RefreshRequest request, long version, string fingerprint, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (_arbiter is null)
                throw new RefreshNotRegisteredException("未注入 IRefreshSink：无法提交写落地仲裁");

            var payload = await RenderAsync(request, ct).ConfigureAwait(false);
            return await _arbiter.CommitAsync(
                new RefreshTicket(request.SlotKey, version, request.TraceId),
                fingerprint,
                () => payload,
                ct).ConfigureAwait(false);
        }

        private async Task<bool> TrackPush(Task<bool> pending)
        {
            Interlocked.Increment(ref _pushes);
            var ok = await pending.ConfigureAwait(false);
            if (!ok) Interlocked.Increment(ref _pushFailures);
            return ok;
        }

        private static Func<TimeSpan, TimerCallback, IDisposable>? ToTimerFactory(IRefreshClock? clock)
            => clock is null
                ? null
                : (interval, callback) => clock.Schedule(interval, () => callback(null));

        /// <summary>停表 + 停兜底轮询 + 注销全部刷新源。</summary>
        public void Dispose()
        {
            foreach (var source in Registry.Sources()) _scheduler.UnregisterSource(source);
            _scheduler.Dispose();
        }
    }
}
