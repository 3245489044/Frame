using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Share.RefreshFramework
{
    /// <summary>
    /// 刷新调度引擎（L1，通用）：dirty 范围合并（**分 High/Low 双车道**）+ 节拍裁决 + 单拍互斥 +
    /// 送达失败置回重试 + 空闲零推送 + 兜底轮询收编，并**集中登记刷新源**（<see cref="IRefreshSource"/>）。
    /// <para>UI 只声明变化源（<see cref="RegisterSource"/>），节拍 / 去重 / 重试 / 优先级由本类统一承担。</para>
    /// <para><b>优先级语义</b>：低优先级（兜底轮询）**不得抢占**高优先级（事件驱动）——同拍内高优先先出，
    /// 低优先仅在无高优先变更时出。</para>
    /// <para>零领域类型：推送动作、在途判定与结果翻转回调由宿主以委托注入；节拍由宿主给定。</para>
    /// </summary>
    public sealed class RefreshScheduler : IDisposable
    {
        /// <summary>单次推送（scope 集合=本拍变更范围，null=全量；false = 未送达 → 置回下一拍重试，at-least-once）。</summary>
        public delegate Task<bool> PushOnce(IReadOnlyCollection<string>? scopes, CancellationToken ct);

        private readonly int _tickMs;
        private readonly PushOnce _pushOnce;
        private readonly Func<bool> _hasInFlight;
        private readonly Action<bool>? _onResultChanged;
        private readonly Func<TimeSpan, TimerCallback, IDisposable> _timerFactory;
        private readonly object _gate = new();
        private readonly HashSet<string> _dirtyKeys = new(StringComparer.Ordinal);   // High 车道
        private readonly HashSet<string> _lowKeys = new(StringComparer.Ordinal);     // Low 车道（兜底轮询）
        private readonly Dictionary<IRefreshSource, Action<string?>> _handlers = new();
        private IDisposable? _timer;
        private IDisposable? _pollTimer;
        private int _ticking;
        private bool _dirtyAll;
        private bool _lowAll;
        private bool? _lastResult;

        /// <param name="tickMs">节拍（ms；契约值由宿主持有）。</param>
        /// <param name="pushOnce">单次推送（scope 集合=本拍变更范围，null=全量；false = 未送达）。</param>
        /// <param name="hasInFlight">是否存在在途实体（在途期间以节拍心跳保底推进度）。</param>
        /// <param name="onResultChanged">结果翻转回调（成功↔失败；接诊断，避免节拍刷屏）。</param>
        /// <param name="timerFactory">定时器工厂（缺省真实定时器；宿主/测试可注入替身）。</param>
        public RefreshScheduler(int tickMs, PushOnce pushOnce, Func<bool> hasInFlight,
            Action<bool>? onResultChanged = null,
            Func<TimeSpan, TimerCallback, IDisposable>? timerFactory = null)
        {
            if (tickMs <= 0) throw new ArgumentOutOfRangeException(nameof(tickMs));
            _tickMs = tickMs;
            _pushOnce = pushOnce ?? throw new ArgumentNullException(nameof(pushOnce));
            _hasInFlight = hasInFlight ?? throw new ArgumentNullException(nameof(hasInFlight));
            _onResultChanged = onResultChanged;
            _timerFactory = timerFactory
                ?? new Func<TimeSpan, TimerCallback, IDisposable>(
                    (interval, callback) => new Timer(callback, null, interval, interval));
        }

        /// <summary>节拍是否在役（定时器已建且未停）。</summary>
        public bool IsRunning { get { lock (_gate) return _timer is not null; } }

        /// <summary>起表（幂等：已在役不重建，保持原节拍相位）。</summary>
        public void Start()
        {
            lock (_gate)
                _timer ??= _timerFactory(TimeSpan.FromMilliseconds(_tickMs), OnTick);
        }

        /// <summary>停表（幂等；dirty 状态保留，重启后仍可补推，不静默失刷）。</summary>
        public void Stop()
        {
            lock (_gate) { _timer?.Dispose(); _timer = null; }
        }

        /// <summary>启用兜底轮询（**低优先级生产者**，受调度器管辖）：每 <paramref name="intervalMs"/> 标记一次低优先级变化；
        /// 低优先级不得抢占事件驱动的高优先级变更。幂等。</summary>
        public void EnableIdlePolling(int intervalMs, string? scopeKey = null)
        {
            if (intervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(intervalMs));
            lock (_gate)
                _pollTimer ??= _timerFactory(TimeSpan.FromMilliseconds(intervalMs),
                    _ => MarkDirty(scopeKey, RefreshPriority.Low));
        }

        /// <summary>停用兜底轮询（幂等）。</summary>
        public void DisableIdlePolling()
        {
            lock (_gate) { _pollTimer?.Dispose(); _pollTimer = null; }
        }

        /// <summary>登记一个刷新源：订阅其 <see cref="IRefreshSource.Changed"/> → 按源优先级合并 dirty；并启动该源（幂等）。</summary>
        public void RegisterSource(IRefreshSource source)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            var priority = source.Priority;
            Action<string?> handler = k => MarkDirty(k, priority);
            lock (_gate)
            {
                if (_handlers.ContainsKey(source)) return;
                _handlers[source] = handler;
                source.Changed += handler;
            }
            source.Start();
        }

        /// <summary>注销刷新源（退订 + 停源）。</summary>
        public void UnregisterSource(IRefreshSource source)
        {
            if (source is null) return;
            Action<string?>? handler;
            lock (_gate)
            {
                if (!_handlers.TryGetValue(source, out handler)) return;
                _handlers.Remove(source);
            }
            source.Changed -= handler;
            source.Stop();
        }

        /// <summary>标记「全量有变化待刷新」（高优先级）。</summary>
        public void MarkDirty() => MarkDirty(null, RefreshPriority.High);

        /// <summary>标记「某范围有变化待刷新」（高优先级）；空/空白键等价于全量。</summary>
        public void MarkDirty(string? scopeKey) => MarkDirty(scopeKey, RefreshPriority.High);

        /// <summary>按优先级标记变化：<see cref="RefreshPriority.Low"/>（兜底轮询）不得抢占 High。</summary>
        public void MarkDirty(string? scopeKey, RefreshPriority priority)
        {
            lock (_gate)
            {
                if (priority == RefreshPriority.Low)
                {
                    if (string.IsNullOrWhiteSpace(scopeKey)) _lowAll = true;
                    else _lowKeys.Add(scopeKey!);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(scopeKey)) _dirtyAll = true;
                    else _dirtyKeys.Add(scopeKey!);
                }
            }
        }

        /// <summary>推送一次刷新请求（全量；返回 false = 未送达）。</summary>
        public Task<bool> PushOnceAsync(CancellationToken ct = default) => _pushOnce(null, ct);

        /// <summary>驱动一拍：高优先车道优先出（低优先不抢占）；无任何 dirty 且无在途 ⇒ 零推送。</summary>
        public async Task TickAsync()
        {
            try
            {
                bool dirty;
                bool low;
                IReadOnlyCollection<string>? scopes;
                lock (_gate)
                {
                    if (_dirtyAll || _dirtyKeys.Count > 0)
                    {
                        dirty = true;
                        low = false;
                        scopes = _dirtyAll ? null : new List<string>(_dirtyKeys);
                        _dirtyAll = false;
                        _dirtyKeys.Clear();
                    }
                    else if (_lowAll || _lowKeys.Count > 0)
                    {
                        dirty = true;
                        low = true;
                        scopes = _lowAll ? null : new List<string>(_lowKeys);
                        _lowAll = false;
                        _lowKeys.Clear();
                    }
                    else
                    {
                        dirty = false;
                        low = false;
                        scopes = null;
                    }
                }
                if (!dirty && !_hasInFlight())
                    return; // 空闲：零推送（事件驱动 + 在途心跳，不做常驻拉取）

                var ok = await _pushOnce(scopes, CancellationToken.None).ConfigureAwait(false);
                ReportIfChanged(ok);
                if (!ok)
                {
                    lock (_gate)
                    {
                        // 未送达：按原车道置回（at-least-once，不静默失刷）
                        if (low)
                        {
                            if (scopes is null) _lowAll = true;
                            else foreach (var s in scopes) _lowKeys.Add(s);
                        }
                        else
                        {
                            if (scopes is null) _dirtyAll = true;
                            else foreach (var s in scopes) _dirtyKeys.Add(s);
                        }
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _ticking, 0);
            }
        }

        private void OnTick(object? state)
        {
            // 单拍互斥：推送耗时超过节拍时跳拍，不叠加并发刷新。
            if (Interlocked.Exchange(ref _ticking, 1) == 1) return;
            _ = TickAsync();
        }

        /// <summary>诊断去抖：仅在推送结果翻转（成功↔失败）时回调，避免节拍刷屏。</summary>
        private void ReportIfChanged(bool ok)
        {
            lock (_gate)
            {
                if (_lastResult == ok) return;
                _lastResult = ok;
            }
            _onResultChanged?.Invoke(ok);
        }

        /// <summary>停表 + 停兜底轮询 + 注销全部刷新源（宿主解体时调用）。</summary>
        public void Dispose()
        {
            IRefreshSource[] sources;
            Action<string?>[] handlers;
            lock (_gate)
            {
                sources = new List<IRefreshSource>(_handlers.Keys).ToArray();
                handlers = new List<Action<string?>>(_handlers.Values).ToArray();
                _handlers.Clear();
            }
            for (var i = 0; i < sources.Length; i++)
            {
                sources[i].Changed -= handlers[i];
                sources[i].Stop();
            }
            DisableIdlePolling();
            Stop();
        }
    }
}
