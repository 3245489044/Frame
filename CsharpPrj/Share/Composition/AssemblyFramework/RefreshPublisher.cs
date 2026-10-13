using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Share.Composition.AssemblyFramework
{
    /// <summary>
    /// 节流刷新发布骨架：dirty 合并（范围化）+ 节拍裁决 + 单拍互斥 + 送达失败置回重试 + 空闲零推送。
    /// <para>dirty 由布尔升级为 <b>范围集合</b>（I3 变更范围化）：<see cref="MarkDirty()"/> 标记全量，
    /// <see cref="MarkDirty(string)"/> 标记单 scope 键；一拍只推送一次，携带本拍累计的 scope 集合（全量 → null）。
    /// 推送动作、在途判定与结果翻转回调均由调用方以委托注入；节拍由调用方给定 —— 本类零领域类型、零领域字面量。</para>
    /// </summary>
    public sealed class RefreshPublisher
    {
        private readonly Func<TimeSpan, TimerCallback, IDisposable> _timerFactory;
        private readonly Func<IReadOnlyCollection<string>?, CancellationToken, Task<bool>> _pushOnce;
        private readonly Func<bool> _hasInFlight;
        private readonly Action<bool>? _onResultChanged;
        private readonly int _tickMs;
        private readonly object _gate = new();
        private readonly HashSet<string> _dirtyKeys = new(StringComparer.Ordinal);
        private IDisposable? _timer;
        private int _ticking;
        private bool _dirtyAll;
        private bool? _lastResult;

        /// <param name="tickMs">节拍（ms；契约值由调用方持有）。</param>
        /// <param name="pushOnce">单次推送（scope 集合=本拍变更范围，null=全量；false = 未送达 → 置回 dirty 下一拍重试）。</param>
        /// <param name="hasInFlight">是否存在在途实体（在途期间以节拍心跳保底推进度）。</param>
        /// <param name="onResultChanged">结果翻转回调（成功↔失败；调用方接诊断，避免节拍刷屏）。</param>
        /// <param name="timerFactory">定时器工厂（缺省真实定时器；宿主/测试可注入替身）。</param>
        public RefreshPublisher(int tickMs,
            Func<IReadOnlyCollection<string>?, CancellationToken, Task<bool>> pushOnce,
            Func<bool> hasInFlight,
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
                    (interval, callback) => new System.Threading.Timer(callback, null, interval, interval));
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

        /// <summary>标记「全量有变化待刷新」：下一拍（≤tickMs）推送（scope=null=全量）。</summary>
        public void MarkDirty() { lock (_gate) _dirtyAll = true; }

        /// <summary>标记「某范围有变化待刷新」（I3）：键去重合并；空/空白键等价于 <see cref="MarkDirty()"/>。</summary>
        public void MarkDirty(string? scopeKey)
        {
            if (string.IsNullOrWhiteSpace(scopeKey)) { MarkDirty(); return; }
            lock (_gate) _dirtyKeys.Add(scopeKey!);
        }

        /// <summary>推送一次刷新请求（全量；返回 false = 未送达）。</summary>
        public Task<bool> PushOnceAsync(CancellationToken ct = default) => _pushOnce(null, ct);

        /// <summary>驱动一拍（定时器回调即调用本方法；亦供宿主/测试显式驱动）。</summary>
        public async Task TickAsync()
        {
            try
            {
                bool dirty;
                IReadOnlyCollection<string>? scopes;
                lock (_gate)
                {
                    dirty = _dirtyAll || _dirtyKeys.Count > 0;
                    scopes = _dirtyAll ? null : (_dirtyKeys.Count > 0 ? new List<string>(_dirtyKeys) : null);
                    _dirtyAll = false;
                    _dirtyKeys.Clear();
                }
                if (!dirty && !_hasInFlight())
                    return; // 空闲：零推送（事件驱动 + 在途心跳，不做常驻拉取）

                var ok = await _pushOnce(scopes, CancellationToken.None).ConfigureAwait(false);
                ReportIfChanged(ok);
                if (!ok)
                {
                    lock (_gate)
                    {
                        // 未送达：保持待刷新（scope 回填）下一拍重试（at-least-once，不静默失刷）
                        if (scopes is null) _dirtyAll = true;
                        else foreach (var s in scopes) _dirtyKeys.Add(s);
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
    }
}