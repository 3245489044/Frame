using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UniversalWorkflowFramework.Relations;
using UniversalWorkflowFramework.Relations.Mounting;

namespace Operation.DataStoreDriven.Relations.Rebuild;

/// <summary>重建动作：operation + 请求（来源固定 <see cref="RelationOrigin.Rebuild"/>）。</summary>
public sealed record RebuildAction(string Operation, RelationRequest Request);

/// <summary>重建报告：按 operation 计数 + 拒绝原因（禁只给总数）；<see cref="Sources"/> = 源可用性一等事实。</summary>
public sealed record RebuildReport(
    string Name,
    bool DryRun,
    IReadOnlyDictionary<string, int> Applied,
    IReadOnlyDictionary<string, int> Skipped,
    IReadOnlyList<string> Rejected)
{
    /// <summary>按来源的可用性/计数（源不可用 ≠ 0 条数据；禁以假 0 冒充）。</summary>
    public IReadOnlyList<RebuildSourceReport> Sources { get; init; } = Array.Empty<RebuildSourceReport>();

    /// <summary>已应用（DryRun 下为「将应用」）总数。</summary>
    public int AppliedTotal => Applied.Values.Sum();

    /// <summary>跳过（幂等未命中）总数。</summary>
    public int SkippedTotal => Skipped.Values.Sum();

    /// <summary>拒绝总数。</summary>
    public int RejectedTotal => Rejected.Count;

    /// <summary>
    /// 源不可用报告（域未注册 / 无列举器）：显式 <c>Available=false</c> + 原因 + 计数键，
    /// 不写入「0 条将应用」的假象（ERR-1 / ERR-2）。
    /// </summary>
    public static RebuildReport Unavailable(
        string name, string domain, string reason, bool dryRun, string skippedKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(skippedKey);

        var skipped = new Dictionary<string, int>(StringComparer.Ordinal) { [skippedKey] = 1 };
        return new RebuildReport(
            name,
            dryRun,
            new Dictionary<string, int>(StringComparer.Ordinal),
            skipped,
            new[] { $"{name}|source-unavailable|{reason}" })
        {
            Sources = new[]
            {
                new RebuildSourceReport(name, domain, Available: false, Read: 0, Applied: 0, Skipped: 1,
                    Rejected: 1, UnavailableReason: reason),
            },
        };
    }
}

/// <summary>重建源事实（可用性 + 计数 + 不可用原因）。</summary>
public sealed record RebuildSourceReport(
    string Name,
    string Domain,
    bool Available,
    int Read,
    int Applied,
    int Skipped,
    int Rejected,
    string? UnavailableReason = null);

/// <summary>重建源：从持久化实体（SQL）读取候选行。</summary>
public interface IRebuildSource<out T>
{
    IAsyncEnumerable<T> ReadAsync(CancellationToken ct);
}

/// <summary>重建投影：领域差异只以数据出现（禁止分支编排）。</summary>
public interface IRebuildProjection<in T>
{
    IEnumerable<RebuildAction> Project(T item);
}

/// <summary>
/// 关系重建引擎：SQL 源 → 投影 → 内核（<see cref="IRelationCommandPort"/>，<c>Origin=Rebuild</c>）。
/// <para>纪律：只调端口不碰图；幂等（duplicate=keep）；<c>DryRun</c> 零写；报告按 operation 计数。</para>
/// </summary>
public sealed class RelationRebuildEngine
{
    private readonly IRelationPorts _ports;
    private readonly IRelationDiagnostics? _diagnostics;
    private readonly MountRebuildExecutor? _mountExecutor;

    /// <param name="ports">关系端口（旧路径；仅测试/迁移夹具使用）。</param>
    /// <param name="diagnostics">诊断出口（可空）。</param>
    /// <param name="mounts">UMF 挂载门面；装配后**全部**动作经 <see cref="MountRebuildExecutor"/>（唯一写路径）。</param>
    /// <param name="bindings">挂载绑定目录（与 <paramref name="mounts"/> 同批装配）。</param>
    public RelationRebuildEngine(
        IRelationPorts ports,
        IRelationDiagnostics? diagnostics = null,
        IMountPort? mounts = null,
        MountBindingCatalog? bindings = null)
    {
        _ports = ports ?? throw new ArgumentNullException(nameof(ports));
        _diagnostics = diagnostics;
        _mountExecutor = mounts is not null && bindings is not null
            ? new MountRebuildExecutor(_ports, bindings, mounts)
            : null;
    }

    /// <summary>执行一次重建（同一 source 重跑计数稳定）。</summary>
    public async Task<RebuildReport> RunAsync<T>(
        string name, IRebuildSource<T> source, IRebuildProjection<T> projection,
        bool dryRun, CancellationToken ct = default, string? domain = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(projection);

        var applied = new Dictionary<string, int>(StringComparer.Ordinal);
        var skipped = new Dictionary<string, int>(StringComparer.Ordinal);
        var rejected = new List<string>();
        var read = 0;

        await foreach (var item in source.ReadAsync(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            read++;

            foreach (var action in projection.Project(item))
            {
                var declared = _ports.Operations.Resolve(action.Operation);
                if (declared is null)
                {
                    Reject(rejected, action.Operation, "undeclared-operation");
                    continue;
                }

                // DryRun：只读声明与投影，不写图（计数 = 将被应用）。
                if (dryRun)
                {
                    Bump(applied, action.Operation);
                    continue;
                }

                var request = action.Request with { Origin = RelationOrigin.Rebuild };

                // UMF · R2：装配门面 ⇒ 唯一写路径 = IMountPort（禁重建直连内核）。
                if (_mountExecutor is not null)
                {
                    var receipt = await _mountExecutor
                        .ExecuteAsync(name, action.Operation, request, ct).ConfigureAwait(false);
                    switch (receipt.Outcome)
                    {
                        case MountOutcome.Committed:
                        case MountOutcome.PartiallyApplied:
                            Bump(applied, action.Operation);
                            break;
                        default:
                            Reject(rejected, action.Operation,
                                $"{receipt.Outcome}|{receipt.Reason ?? string.Empty}");
                            break;
                    }

                    continue;
                }

                var result = declared.IsRemove
                    ? await _ports.Commands.RemoveAsync(action.Operation, request, ct).ConfigureAwait(false)
                    : await _ports.Commands.EnsureAsync(action.Operation, request, ct).ConfigureAwait(false);

                switch (result.Outcome)
                {
                    case RelationOutcome.Applied:
                    case RelationOutcome.Duplicate:
                        Bump(applied, action.Operation);
                        break;
                    case RelationOutcome.NotFound:
                        Bump(skipped, action.Operation);
                        break;
                    default:
                        Reject(rejected, action.Operation,
                            $"{result.Outcome}|{string.Join(";", result.Diagnostics)}");
                        break;
                }
            }
        }

        return new RebuildReport(name, dryRun, applied, skipped, rejected)
        {
            Sources = new[]
            {
                new RebuildSourceReport(
                    name, domain ?? string.Empty, Available: true, Read: read,
                    Applied: applied.Values.Sum(), Skipped: skipped.Values.Sum(), Rejected: rejected.Count),
            },
        };
    }

    private void Reject(List<string> rejected, string operation, string reason)
    {
        rejected.Add($"{operation}|{reason}");
        _diagnostics?.RelationRejected(operation, null, reason);
    }

    private static void Bump(Dictionary<string, int> counts, string key)
        => counts[key] = counts.TryGetValue(key, out var current) ? current + 1 : 1;
}
