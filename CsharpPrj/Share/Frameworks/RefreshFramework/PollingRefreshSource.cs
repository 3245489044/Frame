using System;
using System.Threading;

namespace Share.RefreshFramework
{
    /// <summary>
    /// 兜底轮询刷新源（L1 通用实现，**低优先级生产者**）——按固定周期上报"全量变化"，
    /// 使页面侧原有的自主兜底轮询收编为**受调度器管辖**的低优先级源（不得抢占事件驱动的高优先级推送）。
    /// <para>零领域类型；定时器工厂可注入（测试/宿主替身）。</para>
    /// </summary>
    public sealed class PollingRefreshSource : IRefreshSource, IDisposable
    {
        private readonly int _intervalMs;
        private readonly string? _scopeKey;
        private readonly Func<TimeSpan, TimerCallback, IDisposable> _timerFactory;
        private IDisposable? _timer;

        /// <param name="name">源名。</param>
        /// <param name="intervalMs">轮询周期（ms）。</param>
        /// <param name="scopeKey">变化范围键（null = 全量）。</param>
        /// <param name="timerFactory">定时器工厂（缺省真实定时器）。</param>
        public PollingRefreshSource(string name, int intervalMs, string? scopeKey = null,
            Func<TimeSpan, TimerCallback, IDisposable>? timerFactory = null)
        {
            if (intervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(intervalMs));
            Name = string.IsNullOrWhiteSpace(name) ? "polling" : name;
            _intervalMs = intervalMs;
            _scopeKey = scopeKey;
            _timerFactory = timerFactory
                ?? new Func<TimeSpan, TimerCallback, IDisposable>(
                    (interval, callback) => new Timer(callback, null, interval, interval));
        }

        /// <inheritdoc />
        public string Name { get; }

        /// <summary>兜底轮询恒为低优先级（不得抢占事件驱动变更）。</summary>
        public RefreshPriority Priority => RefreshPriority.Low;

        /// <inheritdoc />
        public event Action<string?>? Changed;

        /// <inheritdoc />
        public void Start()
        {
            lock (this)
                _timer ??= _timerFactory(TimeSpan.FromMilliseconds(_intervalMs), _ => Changed?.Invoke(_scopeKey));
        }

        /// <inheritdoc />
        public void Stop()
        {
            lock (this) { _timer?.Dispose(); _timer = null; }
        }

        /// <inheritdoc />
        public void Dispose() => Stop();
    }
}
