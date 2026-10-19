using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Strategy.Refresh.Model;

namespace Strategy.Refresh.Ports
{
    /// <summary>
    /// 在途租约源（L1 契约，零领域）：域侧把「真实在途实体」投影为租约快照。
    /// <para>框架只消费 <c>Kind=Executable &amp;&amp; Active &amp;&amp; Deadline 未过</c> 的租约；
    /// 无租约 ⇒ 心跳车道关闭，只保留 Change 与 Fallback。</para>
    /// </summary>
    public interface IInFlightSource
    {
        /// <summary>取当前在途租约快照（不得返回 null；无在途返回空表）。</summary>
        ValueTask<IReadOnlyList<InFlightLease>> SnapshotAsync(CancellationToken ct = default);
    }
}
