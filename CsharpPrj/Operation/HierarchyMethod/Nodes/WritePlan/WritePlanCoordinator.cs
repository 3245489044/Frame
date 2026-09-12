using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Center;
using Share.TaskStatusPool.Loops;

namespace Operation.HierarchyMethod.Nodes.WritePlan;

/// <summary>
/// 写计划执行门端口（唯一写门，设计 §4.2 ③）：接收 <see cref="WritePlan"/>，按 (Media, Collection) 分派到唯一写端口，
/// 返回 <see cref="WriteOutcome"/>。计划层只描述、不落库；本接口是计划落库的唯一入口。
/// </summary>
public interface INodeWriteCoordinator
{
    /// <summary>应用一个写计划。任一步失败 → 计划整体拒绝（前序已应用步骤按逆序补偿）。</summary>
    Task<WriteOutcome> ApplyAsync(WritePlan plan, CancellationToken ct = default);
}

/// <summary>
/// 写计划执行门（节点域「计划门」，framework canonical）。
/// <para>
/// 与 <see cref="WriteCoordinator"/>（串行写门**原语**）分层：本类负责计划展开执行 + 介质分派 + 失败归类，
/// 内部**复用** <see cref="WriteCoordinator"/> 做串行单写 + 有界等待（写目标 <see cref="PlanWriteTarget"/>，
/// 与节点执行服务的 <c>ur_nodes.persist</c> 不同 → <see cref="SingleWriteGuard"/> 不误报平行实现）。
/// </para>
/// <para>
/// 原子性口径（**不做假事务**）：<c>INodeStore</c> / <c>IHistoryStore</c> 契约不暴露事务句柄（无 ITransaction 参数），
/// 故本门提供的是「写门串行（消除并发交错）+ 已应用步骤逆序补偿」（AX-CORE.SyncProtocolTriple.Compensate）；
/// 真正的单事务写入需介质端口扩展事务 seam（登记为后续项，不在本轮冒充）。
/// </para>
/// <para>
/// 日志：构造注入 <c>Action&lt;string, object?&gt;</c>（缺省 no-op），由组合根回填具体日志门面（分层无环，框架不感知 Agent 诊断类型）。
/// </para>
/// </summary>
public sealed class WritePlanCoordinator : INodeWriteCoordinator, IDisposable
{
    /// <summary>本门注册的写目标（与节点持久化写者不同，故不触发 fail-fast；写目标收敛批次落地后再合并）。</summary>
    public const string PlanWriteTarget = "ur_nodes.plan";

    /// <summary>本门的写者名（固定 → 多实例幂等，平行实现注册同目标才 fail-fast）。
    /// 字面量与下沉前一致：写者名是 SingleWriteGuard 注册键，改名会改变平行实现的 fail-fast 判据。</summary>
    public const string PlanWriterName = "NodeWriteCoordinator.ApplyAsync";

    private readonly Dictionary<(WriteMedia Media, string Collection), IWriteMediaHandler> _handlers;
    private readonly WriteCoordinator _gate;
    private readonly bool _ownsGate;
    private readonly TimeSpan? _timeout;
    private readonly Action<string, object?>? _log;

    /// <param name="handlers">介质写端口处理器集合（每个 (介质, 集合) 恰一个 → 重复即 fail-fast）。</param>
    /// <param name="gate">串行写门原语；null = 自建（本类负责释放）。</param>
    /// <param name="timeout">写门有界等待预算；null = 门原语缺省。</param>
    /// <param name="log">诊断日志插槽（缺省 no-op）。</param>
    public WritePlanCoordinator(
        IEnumerable<IWriteMediaHandler> handlers,
        WriteCoordinator? gate = null,
        TimeSpan? timeout = null,
        Action<string, object?>? log = null)
    {
        _handlers = BuildHandlerMap(handlers);
        _ownsGate = gate is null;
        _gate = gate ?? new WriteCoordinator();
        _timeout = timeout;
        _log = log;
        SingleWriteGuard.AssertSingleWriter(PlanWriteTarget, PlanWriterName);
    }

    /// <summary>已装配的写端口清单（供装配断言/诊断：每个集合恰一个写者）。</summary>
    public IReadOnlyList<string> WritePorts
        => _handlers.Keys.OrderBy(k => k.Media).ThenBy(k => k.Collection, StringComparer.Ordinal)
            .Select(k => $"{k.Media}/{k.Collection}")
            .ToArray();

    /// <inheritdoc />
    public async Task<WriteOutcome> ApplyAsync(WritePlan plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var applied = new List<string>();
        var skipped = new List<string>();
        try
        {
            if (plan.Steps.Count == 0)
                return WriteOutcome.Rejected(plan.Id, applied, skipped, null,
                    ErrorClass.Irrecoverable, "空计划（无步骤）：意图未展开出任何写步骤。");

            var order = Order(plan);
            return await _gate.RunWriteAsync(PlanWriteTarget,
                innerCt => RunStepsAsync(plan, order, applied, skipped, innerCt),
                _timeout, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 门级失败（有界等待超时等）：归类后作为「无步骤归属」的拒绝返回，不向调用方抛。
            return WriteOutcome.Rejected(plan.Id, applied, skipped, null, ErrorClassifier.ClassifyError(ex), ex.Message);
        }
    }

    /// <summary>
    /// 顺序执行各步骤。承诺：Applied=false 时，<paramref name="applied"/> 中列出的步骤均已按逆序补偿。
    /// </summary>
    private async Task<WriteOutcome> RunStepsAsync(
        WritePlan plan, IReadOnlyList<WriteWork> order,
        List<string> applied, List<string> skipped, CancellationToken ct)
    {
        var skip = new HashSet<string>(plan.Skips.Select(s => s.WorkId), StringComparer.Ordinal);
        foreach (var work in order)
        {
            ct.ThrowIfCancellationRequested();

            if (skip.Contains(work.Id))
            {
                skipped.Add(work.Id); // 被短路 = 前提已满足，其下游依赖照常执行
                continue;
            }

            if (!_handlers.TryGetValue((work.Media, work.Collection), out var handler))
                return WriteOutcome.Rejected(plan.Id, applied, skipped, work.Id,
                    ErrorClass.Irrecoverable, $"未装配介质写端口：{work.Media}/{work.Collection}");

            try
            {
                await handler.ApplyAsync(work, ct).ConfigureAwait(false);
                applied.Add(work.Id);
            }
            catch (Exception ex)
            {
                await CompensateAsync(order, applied, ct).ConfigureAwait(false);
                return WriteOutcome.Rejected(plan.Id, applied, skipped, work.Id,
                    ErrorClassifier.ClassifyError(ex), ex.Message);
            }
        }

        return WriteOutcome.Ok(plan.Id, applied, skipped);
    }

    /// <summary>按逆序补偿已应用步骤（补偿失败不掩盖原始失败，仅留痕观测）。</summary>
    private async Task CompensateAsync(IReadOnlyList<WriteWork> order, IReadOnlyList<string> applied, CancellationToken ct)
    {
        if (applied.Count == 0) return;
        var appliedSet = new HashSet<string>(applied, StringComparer.Ordinal);
        for (var i = order.Count - 1; i >= 0; i--)
        {
            var work = order[i];
            if (!appliedSet.Contains(work.Id) || !_handlers.TryGetValue((work.Media, work.Collection), out var handler))
                continue;
            try
            {
                await handler.CompensateAsync(work, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log?.Invoke("WritePlan_compensate_failed",
                    new { collection = work.Collection, work = work.Id, error = ex.Message });
            }
        }
    }

    /// <summary>构建 (介质, 集合) → 处理器映射；重复拥有同一集合 → fail-fast（违反「每集合恰一个写端口」）。</summary>
    private static Dictionary<(WriteMedia, string), IWriteMediaHandler> BuildHandlerMap(IEnumerable<IWriteMediaHandler> handlers)
    {
        var map = new Dictionary<(WriteMedia, string), IWriteMediaHandler>();
        foreach (var handler in handlers ?? Array.Empty<IWriteMediaHandler>())
        {
            if (handler is null) continue;
            foreach (var collection in handler.Collections)
            {
                if (!map.TryAdd((handler.Media, collection), handler))
                    throw new InvalidOperationException(
                        $"写端口重复装配：{handler.Media}/{collection} 已有写者（每集合恰一个唯一写端口）。");
            }
        }
        return map;
    }

    /// <summary>按 <see cref="WriteWork.Dependencies"/> 稳定拓扑排序（声明序为 tie-break）；环/缺依赖 → 抛。</summary>
    private static IReadOnlyList<WriteWork> Order(WritePlan plan)
    {
        var byId = new Dictionary<string, WriteWork>(StringComparer.Ordinal);
        foreach (var work in plan.Steps)
        {
            if (string.IsNullOrWhiteSpace(work.Id))
                throw new InvalidOperationException("写步骤缺 Id（计划内唯一键）。");
            if (!byId.TryAdd(work.Id, work))
                throw new InvalidOperationException($"写步骤 Id 重复：{work.Id}");
        }

        var result = new List<WriteWork>(byId.Count);
        var done = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        void Visit(WriteWork work)
        {
            if (done.Contains(work.Id)) return;
            if (!visiting.Add(work.Id))
                throw new InvalidOperationException($"写计划存在依赖环：{work.Id}");
            foreach (var dep in work.Dependencies)
            {
                if (!byId.TryGetValue(dep, out var pre))
                    throw new InvalidOperationException($"写步骤 '{work.Id}' 依赖不存在的前序 '{dep}'。");
                Visit(pre);
            }
            visiting.Remove(work.Id);
            done.Add(work.Id);
            result.Add(work);
        }

        foreach (var work in plan.Steps) Visit(work);
        return result;
    }

    public void Dispose()
    {
        if (_ownsGate) _gate.Dispose();
    }
}
