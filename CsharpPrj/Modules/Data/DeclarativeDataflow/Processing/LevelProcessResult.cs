using System;
using System.Collections.Generic;

namespace Modules.DeclarativeDataflow.Processing;

/// <summary>单节点处理结果的类别（区分"处理过"与两类未被处理的控制态，避免把护栏误报为业务失败语义）。</summary>
public enum LevelProcessKind
{
    /// <summary>正常处理（含处理失败）。</summary>
    Processed,

    /// <summary>跳过（已访问去重 / 环守卫拦截），非失败；原因见 <see cref="LevelProcessResult.SkipReason"/>。</summary>
    Skipped,

    /// <summary>被预算（节点数 / 深度）截断，非业务失败但表示展开未穷尽。</summary>
    Truncated,
}

/// <summary>
/// 单节点处理结果（层无关的中立结论）：成败 + 结论词 + 载荷 + 错误 + 类别。
/// <para>
/// 供 <see cref="ILevelProcessor"/> 产出、<see cref="IChildResultAggregator"/> 聚合、
/// 上层消费方（持久化 / 决策）读取；框架自身不解释 <see cref="Payload"/> 的键语义。
/// </para>
/// <para>
/// 字段职责互斥：<see cref="Error"/> 仅承载**失败**原因（无失败为 null）；
/// <see cref="SkipReason"/> 仅承载**跳过**原因（非失败）。二者不得互相借用，
/// 以免出现"成功却带 Error"的误导结论。
/// </para>
/// </summary>
/// <param name="Success">是否成功（<see cref="LevelProcessKind.Truncated"/> 视为未成功，向上暴露"未穷尽"）。</param>
/// <param name="Outcome">结论词（如 pass/fail/continue；层语义由处理器决定）。</param>
/// <param name="Payload">处理载荷（向下传递 / 向上聚合的中立数据）。</param>
/// <param name="Error">失败原因（无失败为 null；跳过原因不写本字段）。</param>
/// <param name="Kind">结果类别（默认 <see cref="LevelProcessKind.Processed"/>）。</param>
/// <param name="SkipReason">跳过原因（仅 <see cref="LevelProcessKind.Skipped"/> 有值）。</param>
public sealed record LevelProcessResult(
    bool Success,
    string? Outcome = null,
    IReadOnlyDictionary<string, object?>? Payload = null,
    string? Error = null,
    LevelProcessKind Kind = LevelProcessKind.Processed,
    string? SkipReason = null)
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyPayload =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>空成功（无处理逻辑的容器层透传结果）。</summary>
    public static readonly LevelProcessResult Passed = new(true);

    /// <summary>载荷（无载荷返回空字典，避免调用方判空）。</summary>
    public IReadOnlyDictionary<string, object?> PayloadOrEmpty => Payload ?? EmptyPayload;

    /// <summary>是否被截断（预算到界）。</summary>
    public bool IsTruncated => Kind == LevelProcessKind.Truncated;

    /// <summary>是否被跳过（去重 / 环守卫）。</summary>
    public bool IsSkipped => Kind == LevelProcessKind.Skipped;

    /// <summary>成功结果。</summary>
    public static LevelProcessResult Ok(string? outcome = null, IReadOnlyDictionary<string, object?>? payload = null)
        => new(true, outcome, payload);

    /// <summary>业务失败结果。</summary>
    public static LevelProcessResult Failed(string error, string? outcome = null)
        => new(false, outcome, null, error);

    /// <summary>跳过结果（已访问去重 / 环守卫；非失败：原因写 <see cref="SkipReason"/>，不写 <see cref="Error"/>）。</summary>
    public static LevelProcessResult Skipped(string reason)
        => new(true, "skipped", Kind: LevelProcessKind.Skipped, SkipReason: reason);

    /// <summary>截断结果（预算到界；表示展开未穷尽）。</summary>
    public static LevelProcessResult Truncated(string reason)
        => new(false, "truncated", null, reason, LevelProcessKind.Truncated);

    /// <summary>
    /// 与子层聚合结论合并：结论词 / 载荷以本节点为主（子层键补充），成败取合取
    /// （任一子层未成功 → 父层未成功），错误取本节点优先、否则首个子层错误；
    /// 子层截断时**结论类别提升为 <see cref="LevelProcessKind.Truncated"/>**，
    /// 使"展开未穷尽"在父 / 根结论上仍可观测（不再只由子层携带）。
    /// <para>
    /// 本节点自身被跳过 / 被截断时不做合并（子树尚未处理或不再展开），直接返回本节点结论。
    /// </para>
    /// </summary>
    public static LevelProcessResult Merge(LevelProcessResult self, LevelProcessResult children)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(children);
        if (self.Kind != LevelProcessKind.Processed) return self;
        return self with
        {
            Success = self.Success && children.Success,
            Outcome = self.Outcome ?? children.Outcome,
            Error = self.Error ?? children.Error,
            Kind = children.Kind == LevelProcessKind.Truncated ? LevelProcessKind.Truncated : self.Kind,
            Payload = MergePayload(self.Payload, children.Payload),
        };
    }

    /// <summary>载荷合并：子层（聚合）键先入、本节点键覆盖同名键，使子层诊断量在父层结论上仍可见。</summary>
    private static IReadOnlyDictionary<string, object?>? MergePayload(
        IReadOnlyDictionary<string, object?>? self, IReadOnlyDictionary<string, object?>? children)
    {
        if (children is null || children.Count == 0) return self;
        if (self is null || self.Count == 0) return children;
        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in children) merged[pair.Key] = pair.Value;
        foreach (var pair in self) merged[pair.Key] = pair.Value;
        return merged;
    }
}
