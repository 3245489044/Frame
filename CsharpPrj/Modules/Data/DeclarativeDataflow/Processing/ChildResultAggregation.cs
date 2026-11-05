using System;
using System.Collections.Generic;
using System.Linq;
using Modules.DeclarativeDataflow.Model;

namespace Modules.DeclarativeDataflow.Processing;

/// <summary>
/// 子层结论聚合器 —— 把"一组子层的结论"收敛为一个父侧可见的结论。
/// <para>
/// 这是"不定层数 → 单一结论"的收敛点：默认实现为合取（任一子层未成功则父侧未成功）。
/// 复杂场景（按 key 取最优 / 因果链尾）由消费方注入自定义实现，框架不新增逻辑算子。
/// </para>
/// <para>
/// 聚合产物经 <see cref="LevelProcessResult.Merge"/> 与父节点结论合并，故本接口返回的
/// 载荷 / 类别须是"可向上合并"的：默认实现给出子层计数（<c>children</c>/<c>skipped</c>/<c>truncated</c>）
/// 并在任一子层截断时把类别置为 <see cref="LevelProcessKind.Truncated"/>。
/// </para>
/// </summary>
public interface IChildResultAggregator
{
    /// <summary>聚合子层结论（空集 = 无子层，视为成功）。<paramref name="node"/> 供按节点定制的实现使用。</summary>
    LevelProcessResult Aggregate(LevelRef node, IReadOnlyList<LevelProcessResult> childResults);
}

/// <summary>默认聚合器：合取（全部子层成功才成功）+ 首个错误透出 + 保留子层计数与类别统计。</summary>
public sealed class CollectChildResultAggregator : IChildResultAggregator
{
    /// <summary>共享实例（无状态）。</summary>
    public static readonly CollectChildResultAggregator Default = new();

    public LevelProcessResult Aggregate(LevelRef node, IReadOnlyList<LevelProcessResult> childResults)
    {
        ArgumentNullException.ThrowIfNull(childResults);
        if (childResults.Count == 0) return LevelProcessResult.Passed;

        var success = true;
        var truncated = false;
        var skipped = 0;
        var counted = 0;
        string? error = null;
        foreach (var result in childResults)
        {
            if (result is null) continue;
            counted++;
            if (!result.Success) success = false;
            if (result.IsTruncated) truncated = true;
            if (result.IsSkipped) skipped++;
            error ??= result.Error;
        }

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["children"] = counted,
            ["skipped"] = skipped,
            ["truncated"] = truncated,
        };
        return new LevelProcessResult(
            Success: success,
            Outcome: "aggregate",
            Payload: payload,
            Error: error,
            Kind: truncated ? LevelProcessKind.Truncated : LevelProcessKind.Processed);
    }
}

/// <summary>
/// AX.ResultComposition 聚合器 —— <c>latest_terminal = argmax_{chain} causal_chain_depth</c>。
/// <para>
/// 在本层次引擎中 causal_chain_depth 由子层结论 payload 的 <c>round</c> 键代理
///（任务层处理器产出推进轮次）：取「推进最深（round 最大）」的子层结论作为最新终态向上代表——
/// 即「以最深层成功终态为准」，而非「全子层合取才成功」。
/// </para>
/// <para>
/// 数据面缺失（子层全无 round / 全部 round=0）时**回退默认合取**（<see cref="CollectChildResultAggregator"/>），
/// 保留计数与类别语义，避免在叶 / 非链层无谓地选首个结论（对齐 AX.ResultComposition 的 argmax 前置要求）。
/// </para>
/// </summary>
public sealed class ResultCompositionAggregator : IChildResultAggregator
{
    /// <summary>共享实例（无状态）。</summary>
    public static readonly ResultCompositionAggregator Default = new();

    public LevelProcessResult Aggregate(LevelRef node, IReadOnlyList<LevelProcessResult> childResults)
    {
        ArgumentNullException.ThrowIfNull(childResults);
        if (childResults.Count == 0) return LevelProcessResult.Passed;

        LevelProcessResult? best = null;
        var bestDepth = int.MinValue;
        var anyDepth = false;
        foreach (var result in childResults)
        {
            if (result is null) continue;
            var depth = ExtractDepth(result);
            if (depth > 0) anyDepth = true;
            if (depth > bestDepth)
            {
                bestDepth = depth;
                best = result;
            }
        }

        // 子层全无链深度（round 缺失 / 全 0）→ 回退默认合取：AX 语义要求 argmax 有可比较的深度，
        // 缺数据时按合取 + 计数处理（保留既有叶/非链层行为，避免选首个误判为终态）。
        return best is null || !anyDepth
            ? CollectChildResultAggregator.Default.Aggregate(node, childResults)
            : best;
    }

    /// <summary>因果链深度：读子层结论 payload 的 <c>round</c> 键；缺失 / 非数值 → 0。</summary>
    private static int ExtractDepth(LevelProcessResult result)
    {
        if (result.Payload is null
            || !result.Payload.TryGetValue("round", out var v)
            || v is null) return 0;
        try { return Convert.ToInt32(v); } catch { return 0; }
    }
}
