using System;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Observe;

/// <summary>
/// 写方协调器（应用层写方唯一入口，不触碰传输）。<c>WriteAsync</c> 把「写-可观测」之间的收敛窗口显式化：
/// 依 <see cref="WriteOptions.Barrier"/> 在 <b>Converged</b>（已收敛）或 <b>Degraded</b>（显式降级）才返回，
/// 绝不在 Applied 后静默宣称成功（领域不变量 I3/I4）。
/// </summary>
public interface IObserveCoordinator
{
    Task<ObserveOutcome> WriteAsync<T>(
        string key,
        T value,
        WriteOptions? options = null,
        CancellationToken ct = default);
}
