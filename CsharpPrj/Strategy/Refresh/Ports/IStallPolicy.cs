using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Strategy.Refresh.Model;

namespace Strategy.Refresh.Ports
{
    /// <summary>
    /// 停滞处置策略（L1 契约）：租约超截止、心跳窗口过期或连续无进展达到阈值时调用一次。
    /// <para>框架默认实现只记录 Warn 并停该槽心跳；域可注入取消轮次 / 标记失败 / 通知 UI。</para>
    /// </summary>
    public interface IStallPolicy
    {
        Task OnStalledAsync(
            string slotKey,
            IReadOnlyList<InFlightLease> leases,
            SlotRefreshState state,
            CancellationToken ct = default);
    }
}
