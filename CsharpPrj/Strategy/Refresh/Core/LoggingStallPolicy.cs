using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Strategy.Refresh.Model;
using Strategy.Refresh.Ports;

namespace Strategy.Refresh.Core
{
    /// <summary>
    /// 默认停滞策略（L1）：只记录一次精简诊断，不取消任何东西。
    /// <para>域侧可替换为「取消轮次 / 标记失败 / 通知页面」的实现；框架不内建领域动作。</para>
    /// </summary>
    public sealed class LoggingStallPolicy : IStallPolicy
    {
        private readonly Action<string>? _report;

        public LoggingStallPolicy(Action<string>? report = null) => _report = report;

        public Task OnStalledAsync(
            string slotKey,
            IReadOnlyList<InFlightLease> leases,
            SlotRefreshState state,
            CancellationToken ct = default)
        {
            var lease = leases.Count > 0 ? leases[0] : null;
            _report?.Invoke(
                $"[RefreshStalled] slot={slotKey} leases={leases.Count} " +
                $"noChange={state.ConsecutiveNoChange} nextDelay={state.NextDelay.TotalMilliseconds}ms " +
                $"lease={lease?.LeaseId ?? "-"} kind={lease?.Kind ?? "-"}");
            return Task.CompletedTask;
        }
    }
}
