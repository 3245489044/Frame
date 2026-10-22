using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Share.CircuitFramework.Execution;
using Share.CircuitFramework.Projection;

namespace Share.CircuitFramework.StageLoop;

/// <summary>
/// 区域阶段循环通用执行器：编译父配方 → 构造区域节点 → 以 WhileTrue 驱动子计划，
/// 跨迭代状态由宿主 <typeparamref name="TState"/> 捕获；判定端口是唯一对电路可见值。
/// </summary>
public static class StageRegionLoopRunner
{
    /// <summary>执行一次区域迭代循环。</summary>
    public static async Task<TResult> RunAsync<TState, TResult>(
        TState state,
        CircuitRecipeSpec parent,
        CircuitRegionSpec region,
        string regionKind,
        string judgePort,
        bool seedContinue,
        Func<TState, CircuitNodeResolver> childResolverFactory,
        Func<TState, TimeSpan, TResult> resultFactory,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(childResolverFactory);
        ArgumentNullException.ThrowIfNull(resultFactory);

        var sw = Stopwatch.StartNew();
        var compiled = CompiledRecipeRegistry.GetOrCompile(parent);
        var regionNode = RegionNodeBase.For(region, childResolverFactory(state).Resolve);
        var parentResolver = new CircuitNodeResolver().Register(regionKind, regionNode);

        var entry = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [judgePort] = seedContinue,
        };

        await CircuitEngine.ExecuteAsync(compiled.Circuit, compiled.Plan, parentResolver.Resolve, entry, ct)
            .ConfigureAwait(false);

        sw.Stop();
        return resultFactory(state, sw.Elapsed);
    }
}
