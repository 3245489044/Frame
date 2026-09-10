using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.DeclarativeDataflow.Budget;
using Modules.DeclarativeDataflow.Model;
using Modules.DeclarativeDataflow.Topology;

namespace Modules.DeclarativeDataflow.Processing;

/// <summary>
/// 多层处理引擎（树折叠 / 递归遍历，LLM-free）—— 框架本体只做三件事：遍历、借出处理权、执法。
/// <para>
/// 每节点：按层解析处理器（层声明或计划覆盖）→ 按方向调用（TopDown 先父后子 / BottomUp 先子后父）
/// → 沿 schema 包含边递归子层 → 聚合子层结论并合并 → 返回。
/// </para>
/// <para>
/// 不定深度的三重护栏（信任反转：不信任层次数据自终止）：
/// ① 环守卫（路径上重复 = 真环，拦截）；② 深度预算（<see cref="ProcessBudget.MaxDepth"/>）；
/// ③ 节点预算（<see cref="ProcessBudget.MaxNodes"/>）。任一触发即返回截断结论，不继续展开。
/// </para>
/// <para>框架不写边、不落库：持久化由处理器经上层注入端口完成（TH-12 单一写边 owner）。</para>
/// </summary>
public sealed class HierarchyProcessEngine : IHierarchyProcessEngine
{
    private readonly RelationalHierarchyTopology _topology;
    private readonly ILevelProcessorRegistry _processors;
    private readonly IChildResultAggregator _aggregator;
    private readonly ILevelProcessObserver? _observer;

    /// <param name="topology">层次实例拓扑（结构 + 实例关系）。</param>
    /// <param name="processors">层处理器注册表（按层声明的 ProcessId 解析；未命中 = 容器层透传）。</param>
    /// <param name="aggregator">子层结论聚合器（缺省合取）。</param>
    /// <param name="observer">可选观测（诊断 / 测试）。</param>
    public HierarchyProcessEngine(
        RelationalHierarchyTopology topology,
        ILevelProcessorRegistry processors,
        IChildResultAggregator? aggregator = null,
        ILevelProcessObserver? observer = null)
    {
        _topology = topology ?? throw new ArgumentNullException(nameof(topology));
        _processors = processors ?? throw new ArgumentNullException(nameof(processors));
        _aggregator = aggregator ?? CollectChildResultAggregator.Default;
        _observer = observer;
    }

    /// <inheritdoc />
    public async Task<LevelProcessResult> ProcessAsync(
        LevelRef root, HierarchyProcessPlan? plan = null, CancellationToken ct = default)
    {
        root.EnsureValid();
        var effectivePlan = plan ?? HierarchyProcessPlan.Default;
        var budget = effectivePlan.EffectiveBudget;
        var state = new RunState();
        return await ProcessNodeAsync(root, LevelPath.Root(root), upstream: null, effectivePlan, budget, state, ct)
            .ConfigureAwait(false);
    }

    private async Task<LevelProcessResult> ProcessNodeAsync(
        LevelRef node,
        LevelPath path,
        LevelProcessResult? upstream,
        HierarchyProcessPlan plan,
        ProcessBudget budget,
        RunState state,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // 护栏②：深度（不定深度的第一道防线；路径局部判定，不写运行态）。
        if (budget.IsDepthLimitExceeded(path.Depth))
        {
            _observer?.OnGuardTriggered(node, "max-depth");
            return LevelProcessResult.Truncated($"max-depth({budget.MaxDepth})");
        }

        // 去重：同一实体在本次处理中至多处理一次（DAG 共享节点 / 重复抵达归此）。
        if (!state.Visited.Add(node))
            return LevelProcessResult.Skipped("already-visited");

        // 护栏③：节点总数（按实体去重的处理节点数，含根；计数一旦越界即截断，
        // 其后节点必然同样越界且不再展开，故运行走向必然收敛）。
        state.NodeCount++;
        if (budget.IsNodeLimitExceeded(state.NodeCount))
        {
            _observer?.OnGuardTriggered(node, "node-budget");
            return LevelProcessResult.Truncated($"node-budget({budget.MaxNodes})");
        }

        _observer?.OnEnter(node, path);
        var processor = ResolveProcessor(node, plan);

        LevelProcessResult self;
        IReadOnlyList<LevelProcessResult> childResults;
        if (plan.Order == HierarchyTraversalOrder.TopDown)
        {
            // 父决定子：先出本节点结论，作为下游下传给子层。
            self = await InvokeAsync(processor, node, path, upstream, Array.Empty<LevelProcessResult>(), plan, ct)
                .ConfigureAwait(false);
            childResults = await ProcessChildrenAsync(node, path, self, plan, budget, state, ct).ConfigureAwait(false);
        }
        else
        {
            // 子收敛到父：先出全部子层结论，再交给本节点聚合。
            childResults = await ProcessChildrenAsync(node, path, upstream, plan, budget, state, ct).ConfigureAwait(false);
            self = await InvokeAsync(processor, node, path, upstream, childResults, plan, ct).ConfigureAwait(false);
        }

        var result = LevelProcessResult.Merge(self, _aggregator.Aggregate(node, childResults));
        _observer?.OnExit(node, path, result);
        return result;
    }

    private ILevelProcessor? ResolveProcessor(LevelRef node, HierarchyProcessPlan plan)
    {
        var processId = plan.ProcessIdOverride ?? _topology.RawSchema.Level(node.LevelId)?.ProcessId;
        return _processors.Resolve(processId);
    }

    private async Task<IReadOnlyList<LevelProcessResult>> ProcessChildrenAsync(
        LevelRef node,
        LevelPath path,
        LevelProcessResult? upstream,
        HierarchyProcessPlan plan,
        ProcessBudget budget,
        RunState state,
        CancellationToken ct)
    {
        var results = new List<LevelProcessResult>();
        if (!string.IsNullOrWhiteSpace(plan.StopLevelId) &&
            string.Equals(node.LevelId, plan.StopLevelId, StringComparison.Ordinal))
            return results; // 展开止层：本层被处理，但不再向下展开

        var children = await _topology.ChildrenAsync(node, ct).ConfigureAwait(false);
        foreach (var child in children)
        {
            ct.ThrowIfCancellationRequested();

            // 护栏①：环守卫（路径上已出现 = 真环；自环层的合法递归不会命中此分支）。
            // 注：终止性由全局 Visited 去重保证；本判定不改变可达集，只把"重复抵达"细分为
            // "真环"（cycle-detected）与"DAG 共享"（already-visited）两种可观测原因。
            if (path.Contains(child))
            {
                _observer?.OnGuardTriggered(child, "cycle-detected");
                results.Add(LevelProcessResult.Skipped("cycle-detected"));
                continue;
            }

            var childPath = path.Append(child);
            results.Add(await ProcessNodeAsync(child, childPath, upstream, plan, budget, state, ct).ConfigureAwait(false));
        }
        return results;
    }

    private static async Task<LevelProcessResult> InvokeAsync(
        ILevelProcessor? processor,
        LevelRef node,
        LevelPath path,
        LevelProcessResult? upstream,
        IReadOnlyList<LevelProcessResult> childResults,
        HierarchyProcessPlan plan,
        CancellationToken ct)
    {
        if (processor is null) return LevelProcessResult.Passed; // 无处理器 = 容器层透传
        var context = new LevelProcessContext(plan, path, node, upstream, childResults);
        var result = await processor.ProcessAsync(node, context, ct).ConfigureAwait(false);
        return result ?? LevelProcessResult.Failed($"层处理器 '{processor.ProcessId}' 返回 null。");
    }

    /// <summary>单次处理的运行态（去重集 + 节点计数）。</summary>
    private sealed class RunState
    {
        /// <summary>已处理实体（按实体值相等去重，与 Key 字符串编码解耦）。</summary>
        public HashSet<LevelRef> Visited { get; } = new();

        /// <summary>已纳入处理预算的节点数（含根；按实体去重后递增）。</summary>
        public int NodeCount { get; set; }
    }
}
