using System;
using System.Threading;
using System.Threading.Tasks;

namespace Share.Composition.AssemblyFramework
{
    /// <summary>
    /// 节流刷新发布骨架：dirty 合并 + 节拍裁决 + 单拍互斥 + 送达失败置回重试 + 空闲零推送。
    /// <para>推送动作（页面/通道解析、未挂载即静默返回 false）、在途判定与结果翻转回调均由调用方以委托注入；
    /// 节拍由调用方给定 —— 本类零领域类型、零领域字面量。</para>
    /// </summary>
    public sealed class RefreshPublisher
    {
        private readonly Func<TimeSpan, TimerCallback, IDisposable> _timerFactory;
        private readonly Func<CancellationToken, Task<bool>> _pushOnce;
        private readonly Func<bool> _hasInFlight;
        private readonly Action<bool>? _onResultChanged;
        private readonly int _tickMs;
        private readonly object _gate = new();
        private IDisposable? _timer;
        private int _ticking;
        private bool _dirty;
        private bool? _lastResult;

        /// <param name="tickMs">节拍（ms；契约值由调用方持有）。</param>
        /// <param name="pushOnce">单次推送（调用方解析页面/通道；false = 未送达 → 置回 dirty 下一拍重试）。</param>
        /// <param name="hasInFlight">是否存在在途实体（在途期间以节拍心跳保底推进度）。</param>
        /// <param name="onResultChanged">结果翻转回调（成功↔失败；调用方接诊断，避免节拍刷屏）。</param>
        /// <param name="timerFactory">定时器工厂（缺省真实定时器；宿主/测试可注入替身）。</param>
        public RefreshPublisher(int tickMs,
            Func<CancellationToken, Task<bool>> pushOnce,
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

        /// <summary>标记「有变化待刷新」：下一拍（≤tickMs）推送；窗口内多次标记合并为一次。</summary>
        public void MarkDirty() { lock (_gate) _dirty = true; }

        /// <summary>推送一次刷新请求；返回 false = 未送达（页面未挂载 / 通道未就绪 / 推送异常）。</summary>
        public Task<bool> PushOnceAsync(CancellationToken ct = default) => _pushOnce(ct);

        /// <summary>驱动一拍（定时器回调即调用本方法；亦供宿主/测试显式驱动）。</summary>
        public async Task TickAsync()
        {
            try
            {
                bool dirty;
                lock (_gate) { dirty = _dirty; _dirty = false; }
                if (!dirty && !_hasInFlight())
                    return; // 空闲：零推送（事件驱动 + 在途心跳，不做常驻拉取）

                var ok = await _pushOnce(CancellationToken.None).ConfigureAwait(false);
                ReportIfChanged(ok);
                if (!ok)
                {
                    lock (_gate) _dirty = true; // 未送达：保持待刷新，下一拍重试（at-least-once，不静默失刷）
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
