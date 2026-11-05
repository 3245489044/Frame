using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Threading.Diagnostics
{
    /// <summary>
    /// 卡死自取证 watchdog（R6 内建观测，自 Agent.Core 迁入）：监控一段执行（run/round/step）在 <see cref="_threshold"/> 内无进展
    /// （无心跳）即判定卡死，经注入的诊断插槽产出诊断快照（阶段定位 + 心跳停滞点 + 线程数）。
    /// 使「存储写 / 锁等待 / UI 封送」任一卡点在**同一次运行**即可定位，无需手动 dump 或多次重跑；取证不阻塞主循环。
    /// <para>
    /// 框架内核零 Agent 依赖：只经 <see cref="Action{T1,T2}"/> 插槽触达诊断；日志落盘 / run_events 写区（如 Agent 侧
    /// AgentLog + RunEvent 契约）由装配方在插槽内完成。
    /// </para>
    /// 用法：Heartbeat(阶段) 在进展点调用（每次重置计时）；Complete() 正常结束；Dispose() 幂等收尾。
    /// 局限：托管环境无法直接采样另一线程的托管栈，故诊断以「最后心跳阶段」为卡点定位。
    /// </summary>
    public sealed class StallDetector : IDisposable
    {
        private readonly string _stage;
        private readonly TimeSpan _threshold;
        private readonly Action<string, object?>? _diagnostic;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _monitor;
        private volatile string _lastHeartbeat = "(start)";
        private int _completed;
        private int _disposed;

        /// <param name="stage">监控阶段标签（如 "run-loop" / "round" / "step"）。</param>
        /// <param name="threshold">无心跳判定阈值。</param>
        /// <param name="diagnostic">诊断插槽 (stage, payload)；null = 静默（仅心跳复位计时）。</param>
        public StallDetector(string stage, TimeSpan threshold, Action<string, object?>? diagnostic = null)
        {
            _stage = stage ?? throw new ArgumentNullException(nameof(stage));
            _threshold = threshold > TimeSpan.Zero ? threshold : TimeSpan.FromSeconds(30);
            _diagnostic = diagnostic;
            _monitor = Task.Run(MonitorLoop);
        }

        /// <summary>进展心跳：调用方在进展点（如每轮完成、每步完成）调用，重置卡死计时并记录停滞定位点。</summary>
        public void Heartbeat(string? progressPoint)
        {
            _lastHeartbeat = progressPoint ?? _lastHeartbeat;
            _clock.Restart();
        }

        /// <summary>正常结束：停止卡死监控（幂等）。</summary>
        public void Complete()
        {
            if (Interlocked.Exchange(ref _completed, 1) == 0)
                _clock.Stop();
        }

        /// <summary>后台监控：间隔阈值检查，无心跳超阈即产出诊断（单次运行自证定位，不阻塞主循环）。</summary>
        private async Task MonitorLoop()
        {
            var ct = _cts.Token;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(_threshold, ct).ConfigureAwait(false);
                    if (_completed == 1) break;
                    if (_clock.Elapsed > _threshold)
                        ReportStall();
                }
            }
            catch (OperationCanceledException) { /* 正常收尾 */ }
        }

        private void ReportStall()
        {
            var payload = new
            {
                stage = _stage,
                stalledAt = _lastHeartbeat,
                elapsedSinceHeartbeatMs = (long)_clock.Elapsed.TotalMilliseconds,
                managedThreadCount = ThreadPool.ThreadCount,
            };
            _diagnostic?.Invoke(_stage, payload);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Complete();
            _cts.Cancel();
            try { _monitor.GetAwaiter().GetResult(); }
            catch { /* 收尾异常忽略 */ }
            _cts.Dispose();
        }
    }
}