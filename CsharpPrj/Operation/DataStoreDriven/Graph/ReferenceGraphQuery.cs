using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.Core;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using RefModels = UniversalWorkflowFramework.Reference.Models;

namespace Operation.DataStoreDriven.Graph;

/// <summary>
/// 引用图**只读算法服务**（B1）—— 写边 owner（<see cref="ReferenceGraphCoordinator"/>）之上的只读面：
/// ① 路径 / 影响域 = **委托 owner 的内存邻接有界遍历**（单一实现，本类不复制遍历）；
/// ② 环检测 / 图完整性 = 薄转发 UniversalReference 的 <see cref="CircularDependencyDetector"/> /
/// <see cref="GraphValidator{TNodeId}"/>（这两项原仓无实现，故为其唯一落点）。本类只读、不写边、不编排
/// （编排恒在应用层 <c>[Flow]</c>）。
/// <para>
/// 消费面：维护面 <c>graph.maintain=status/diff</c> 的影响域 / 环报告；调用方**按需触发**（不进热路径，R4）。
/// </para>
/// <para>
/// 信任反转：全部遍历受 <see cref="_maxDepth"/> + <see cref="_maxNodes"/> 双重上限收口 ——
/// **不信任调用方给出的无界深度/规模**；超限即截断（结果为「已截断」的有限集，不抛）。
/// </para>
/// </summary>
public sealed class ReferenceGraphQuery
{
    private readonly ReferenceGraphCoordinator _owner;
    private readonly int _maxDepth;
    private readonly int _maxNodes;

    /// <param name="owner">写边 owner（其内存邻接即唯一遍历实现；本类不写、不复制遍历）。</param>
    /// <param name="maxDepth">遍历深度上限（缺省 8）。</param>
    /// <param name="maxNodes">遍历规模上限（缺省 2000 节点）。</param>
    public ReferenceGraphQuery(ReferenceGraphCoordinator owner, int maxDepth = 8, int maxNodes = 2000)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _maxDepth = maxDepth > 0 ? maxDepth : 8;
        _maxNodes = maxNodes > 0 ? maxNodes : 2000;
    }

    private IResourceReferenceStore Port => _owner;

    /// <summary>取邻接键（<paramref name="outgoing"/>=true 取出边目标，false 取入边源）。</summary>
    /// <param name="key">端点唯一键（<c>{type}:{id}</c>）。</param>
    /// <param name="outgoing">方向。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<IReadOnlyList<string>> GetNeighborsAsync(string key, bool outgoing = true, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var refs = await Port.GetReferencesAsync(ReferenceGraphCoordinator.KeyToIdentifier(key), includeIncoming: true, ct)
            .ConfigureAwait(false);
        return refs
            .Where(r => r.IsIncoming != outgoing)
            .Select(r => (outgoing ? r.Target : r.Source).GetUniqueKey())
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>路径可达性（单一实现 = owner 的有界 DFS；本方法只是其布尔投影）。</summary>
    /// <param name="from">起点唯一键。</param>
    /// <param name="to">终点唯一键。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<bool> HasPathAsync(string from, string to, CancellationToken ct = default)
        => (await FindPathsAsync(from, to, ct).ConfigureAwait(false)).Count > 0;

    /// <summary>全部路径（**单一实现** = <see cref="ReferenceGraphCoordinator.FindPathsAsync"/> 的内存邻接有界 DFS；
    /// 本方法不复制遍历逻辑，只做签名归一）。</summary>
    /// <param name="from">起点唯一键。</param>
    /// <param name="to">终点唯一键。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<IReadOnlyList<IReadOnlyList<string>>> FindPathsAsync(string from, string to, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        var paths = await _owner.FindPathsAsync(from, to, _maxDepth, ct).ConfigureAwait(false);
        return paths.Select(p => (IReadOnlyList<string>)p).ToArray();
    }

    /// <summary>影响域（起点可达集；**单一实现** = <see cref="ReferenceGraphCoordinator.GetDependenciesAsync"/>）。</summary>
    /// <param name="key">起点唯一键。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<IReadOnlyList<string>> GetImpactDomainAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var reachable = await _owner.GetDependenciesAsync(key, ct).ConfigureAwait(false);
        return reachable.OrderBy(k => k, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// 环检测（转发 UR <see cref="CircularDependencyDetector.Analyze"/>）—— **按需调用**，作用域由调用方给定
    /// （如某 task 的节点键集合）；邻接在作用域内**有界**展开后交纯内存算法（禁「取全量后再过滤」的无界形态）。
    /// </summary>
    /// <param name="keys">作用域键集合（环检测节点面）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<RefModels.CircularDependencyResult> AnalyzeCyclesAsync(IEnumerable<string> keys, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var scope = keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).Take(_maxNodes).ToArray();
        var adjacency = await LoadOutgoingAsync(scope, ct).ConfigureAwait(false);
        return CircularDependencyDetector.Analyze(scope, k => Neighbors(adjacency, k));
    }

    /// <summary>图完整性校验（转发 UR <see cref="GraphValidator{TNodeId}.ValidateIntegrity"/>：单起点 / 单终点 / 边端点存在）。</summary>
    /// <param name="keys">作用域键集合。</param>
    /// <param name="isStart">起点判定。</param>
    /// <param name="isEnd">终点判定。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<ValidationResult> ValidateIntegrityAsync(
        IEnumerable<string> keys, Func<string, bool> isStart, Func<string, bool> isEnd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(isStart);
        ArgumentNullException.ThrowIfNull(isEnd);
        var scope = keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).Take(_maxNodes).ToArray();
        var adjacency = await LoadOutgoingAsync(scope, ct).ConfigureAwait(false);
        var edges = adjacency.SelectMany(kv => kv.Value.Select(t => (Source: kv.Key, Target: t)));
        return new GraphValidator<string>().ValidateIntegrity(scope, edges, isStart, isEnd);
    }

    /// <summary>作用域内有界展开出边邻接（BFS；双上限收口：节点数 ≤ <see cref="_maxNodes"/>、深度 ≤ <see cref="_maxDepth"/>）。</summary>
    private async Task<Dictionary<string, List<string>>> LoadOutgoingAsync(IEnumerable<string> seeds, CancellationToken ct)
    {
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var frontier = new List<string>();
        foreach (var seed in seeds)
        {
            if (adjacency.Count >= _maxNodes) break;
            if (adjacency.TryAdd(seed, new List<string>())) frontier.Add(seed);
        }

        for (var depth = 0; depth < _maxDepth && frontier.Count > 0; depth++)
        {
            var next = new List<string>();
            foreach (var key in frontier)
            {
                ct.ThrowIfCancellationRequested();
                var refs = await Port.GetReferencesAsync(ReferenceGraphCoordinator.KeyToIdentifier(key), includeIncoming: true, ct)
                    .ConfigureAwait(false);
                foreach (var reference in refs)
                {
                    if (reference.IsIncoming) continue;
                    var target = reference.Target.GetUniqueKey();
                    if (string.IsNullOrWhiteSpace(target)) continue;

                    if (adjacency.TryGetValue(key, out var outgoing)
                        && !outgoing.Contains(target, StringComparer.Ordinal))
                        outgoing.Add(target);

                    if (adjacency.Count >= _maxNodes) continue;
                    if (adjacency.TryAdd(target, new List<string>())) next.Add(target);
                }
            }
            frontier = next;
        }

        return adjacency;
    }

    private static IEnumerable<string> Neighbors(IReadOnlyDictionary<string, List<string>> adjacency, string key)
        => adjacency.TryGetValue(key, out var outgoing) ? outgoing : Enumerable.Empty<string>();
}
