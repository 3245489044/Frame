using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.DeclarativeDataflow.Budget;
using Modules.DeclarativeDataflow.Model;
using Modules.DeclarativeDataflow.Processing;
using Modules.DeclarativeDataflow.Topology;

namespace Strategy.Catalog.Hierarchy;

/// <summary>
/// 层次实例展开结果 —— 每一层到达了多少实例（= 目录里「该层有多少条」，供二级索引展示）。
/// </summary>
/// <param name="RootKey">展开起点（<see cref="LevelRef.Key"/>）。</param>
/// <param name="CountsByLevel">层 Id → 到达实例数（序 = 层声明序）。</param>
/// <param name="ProcessedNodes">本次处理节点总数（含根）。</param>
/// <param name="Truncated">是否被预算截断（**可见化**：展开未穷尽）。</param>
/// <param name="Error">失败原因（无失败为 null）。</param>
public sealed record HierarchyExpansion(
    string RootKey,
    IReadOnlyDictionary<string, int> CountsByLevel,
    int ProcessedNodes,
    bool Truncated,
    string? Error);

/// <summary>
/// 层次实例展开器 —— 用 <see cref="HierarchyProcessEngine"/> 跑一趟真实层次（实例级），
/// 产出「各层实例数」这一二级索引所需的量化事实。
/// <para>
/// **不含遍历逻辑**：遍历 / 环守卫 / 深度预算 / 节点预算全部由 <c>Modules.DeclarativeDataflow</c> 提供
/// （守「勿再复制层遍历逻辑」）；本类只装配引擎 + 计数处理器。
/// </para>
/// <para>未装配 <see cref="IHierarchyRelationReader"/> ⇒ 只能到达根节点（结构投影仍可经 <see cref="HierarchyCatalogSource"/> 使用）。</para>
/// </summary>
public sealed class HierarchyCatalogExpander
{
    /// <summary>计数处理器 Id（内部使用；经 ProcessIdOverride 覆盖全部层）。</summary>
    public const string CountingProcessId = "catalog:count";

    private readonly HierarchySchema _schema;
    private readonly IHierarchyRelationReader? _reader;

    /// <param name="schema">层声明。</param>
    /// <param name="reader">实例关系读取端口（可空；空 = 只能到达根）。</param>
    public HierarchyCatalogExpander(HierarchySchema schema, IHierarchyRelationReader? reader = null)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _reader = reader;
    }

    /// <summary>
    /// 展开以 <paramref name="root"/> 为根的层次，统计各层实例数。
    /// </summary>
    /// <param name="root">起点（通常 = 根层 + 作用域键）。</param>
    /// <param name="budget">预算（null = <see cref="ProcessBudget.Default"/>，不定深度必配）。</param>
    /// <param name="stopLevelId">展开止层（null = 按 schema 展开到底）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<HierarchyExpansion> ExpandAsync(
        LevelRef root,
        ProcessBudget? budget = null,
        string? stopLevelId = null,
        CancellationToken ct = default)
    {
        root.EnsureValid();

        var counters = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var topology = new RelationalHierarchyTopology(new SchemaTopology(_schema), _reader);
        var registry = new LevelProcessorRegistry(new CountingLevelProcessor(CountingProcessId, counters));
        var engine = new HierarchyProcessEngine(topology, registry);

        var plan = HierarchyProcessPlan.BottomUp(
            budget ?? ProcessBudget.Default,
            processIdOverride: CountingProcessId,
            stopLevelId: stopLevelId);

        try
        {
            var result = await engine.ProcessAsync(root, plan, ct).ConfigureAwait(false);

            // 层序：按 schema 层声明序输出（保证呈现稳定，不受遍历先后影响）。
            var ordered = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var level in _schema.Levels)
            {
                if (level is null || string.IsNullOrWhiteSpace(level.LevelId)) continue;
                ordered[level.LevelId] = counters.TryGetValue(level.LevelId, out var n) ? n : 0;
            }

            return new HierarchyExpansion(root.Key, ordered,
                ordered.Values.Sum(),
                result.IsTruncated,
                result.Success ? null : result.Error);
        }
        catch (Exception ex)
        {
            // 引擎不抛为承诺；此处是最后一道显式兜底（不吞：错误进结果，成败对调用方可见）。
            return new HierarchyExpansion(root.Key, new Dictionary<string, int>(StringComparer.Ordinal), 0, false, ex.Message);
        }
    }

    /// <summary>计数层处理器（每个节点到达即 +1；结论载荷带层 Id 与层内序号，供上层诊断）。</summary>
    private sealed class CountingLevelProcessor(
        string processId, ConcurrentDictionary<string, int> counters) : ILevelProcessor
    {
        public string ProcessId { get; } = processId;

        public Task<LevelProcessResult> ProcessAsync(
            LevelRef node, ILevelProcessContext context, CancellationToken ct = default)
        {
            var count = counters.AddOrUpdate(node.LevelId, 1, (_, current) => current + 1);
            return Task.FromResult(LevelProcessResult.Ok(
                outcome: node.LevelId,
                payload: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["level"] = node.LevelId,
                    ["count"] = count,
                    ["depth"] = context.Depth,
                }));
        }
    }
}
