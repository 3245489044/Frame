using System.Threading;
using System.Threading.Tasks;
using Modules.Node.Relations;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 引用关系写外盒端口（G8 / R1 安全底线）：实例层写实体（SQLite）同时登记关系意图，跨介质（SQLite ↔ LiteDB）
/// 无单库原子性时由外盒提供**补偿 / 重放**，替代「从 nodes（RelationsJson）重建」的兜底。
/// </summary>
public interface IReferenceOutbox
{
    /// <summary>登记一条待写边（与实体写同批；幂等）。</summary>
    Task EnqueueAsync(StaticReference edge, CancellationToken ct = default);

    /// <summary>冲刷全部待写边（幂等重放；成功后清空），返回落库边数。</summary>
    Task<int> FlushAsync(CancellationToken ct = default);
}
