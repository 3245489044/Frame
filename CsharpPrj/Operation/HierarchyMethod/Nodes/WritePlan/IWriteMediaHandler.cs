using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.HierarchyMethod.Nodes.WritePlan;

/// <summary>
/// 唯一写端口处理器：一个处理器拥有某介质下的一组集合（<see cref="Collections"/>），
/// 由写门按 (Media, Collection) 静态分派——「每个集合恰有一个写端口」（设计 §4.1 R2）。
/// 实现方**只**调用该集合的既有单写者，不得新增写路径。
/// </summary>
public interface IWriteMediaHandler
{
    /// <summary>所属介质。</summary>
    WriteMedia Media { get; }

    /// <summary>本处理器拥有的集合（见 <see cref="WriteCollections"/>）。</summary>
    IReadOnlyList<string> Collections { get; }

    /// <summary>应用一个写步骤（幂等 upsert / delete）。失败向上抛，由写门统一归类。</summary>
    Task ApplyAsync(WriteWork work, CancellationToken ct);

    /// <summary>
    /// 补偿（AX-CORE.SyncProtocolTriple.Compensate）：撤销本步骤已产生的副作用，供计划整体拒绝时按逆序调用。
    /// 缺省 no-op（如幂等 upsert 无逆操作；删除类步骤无 pre-image 契约时不提供逆操作）。
    /// </summary>
    Task CompensateAsync(WriteWork work, CancellationToken ct);
}

/// <summary>介质处理器基类：<see cref="CompensateAsync"/> 缺省 no-op，实现方仅在有真实逆操作时覆盖。</summary>
public abstract class WriteMediaHandlerBase : IWriteMediaHandler
{
    /// <inheritdoc />
    public abstract WriteMedia Media { get; }

    /// <inheritdoc />
    public abstract IReadOnlyList<string> Collections { get; }

    /// <inheritdoc />
    public abstract Task ApplyAsync(WriteWork work, CancellationToken ct);

    /// <inheritdoc />
    public virtual Task CompensateAsync(WriteWork work, CancellationToken ct) => Task.CompletedTask;
}
