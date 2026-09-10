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
