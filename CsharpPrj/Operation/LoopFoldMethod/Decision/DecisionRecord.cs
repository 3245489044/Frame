using System;

namespace Operation.LoopFoldMethod.Decision;

/// <summary>决策记录种类（可观测归因的裁决结果域）。</summary>
public enum DecisionRecordKind
{
    /// <summary>规则劝排除（缺工具/去重命中导致规则跳过，R4：silent-fallback 显式化）。</summary>
    RuleExcluded,

    /// <summary>规则集回退（文件→内联/默认；缺工具后落下条/默认）。</summary>
    RuleFallback,

    /// <summary>tool_call 命中但工具未装配（R4 缺工具显式中止，不回退）。</summary>
    ToolCallMissing,

    /// <summary>工具执行异常/返回空（R7，归因可见）。</summary>
    ToolCallError,

    /// <summary>工具执行成功。</summary>
    ToolCallOk,

    /// <summary>导演/解析失败（R7：空 stay 不再吞成 Success）。</summary>
    DirectorParseFailed,

    /// <summary>导演/调度正常解析。</summary>
    DirectorResolved,

    /// <summary>常规决策推进（continue/stop/error 之一）。</summary>
    Decision,

    /// <summary>显式中止（Fail/Abort，R7 非成功）。</summary>
    DecisionAborted,
}

/// <summary>决策记录严重度（决定是否告警；Warning+ 触发 <c>Alert</c>）。</summary>
public enum DecisionSeverity
{
    /// <summary>信息（正常推进，无告警）。</summary>
    Info = 0,

    /// <summary>警告（降级/回退/缺工具，需可见）。</summary>
    Warning = 1,

    /// <summary>错误（执行失败/解析失败，需告警）。</summary>
    Error = 2,
}

/// <summary>
/// 决策可观测记录（LLM-free、纯数据）—— 在规则排除/回退、导演解析失败、工具调用缺/错等裁决降级点时产出，
/// 供审计（内存承载）与告警（Warning+）消费。对齐 R5/R7：降级不再不可见、不再等价成功。
/// </summary>
public sealed record DecisionRecord(
    DateTimeOffset Timestamp,
    DecisionRecordKind Kind,
    DecisionSeverity Severity,
    string? RuleId = null,
    string? ToolName = null,
    string? Reason = null,
    string? FolderId = null)
{
    /// <summary>是否需告警（<see cref="DecisionSeverity.Warning"/> 及以上）。</summary>
    public bool IsAlert => Severity >= DecisionSeverity.Warning;

    /// <summary>便捷构造：按种类推导严重度。</summary>
    public static DecisionRecord New(
        DecisionRecordKind kind,
        string? ruleId = null, string? toolName = null, string? reason = null,
        string? folderId = null, DateTimeOffset? ts = null)
        => new(ts ?? DateTimeOffset.Now, kind, SeverityOf(kind), ruleId, toolName, reason, folderId);

    /// <summary>种类 → 缺省严重度。</summary>
    private static DecisionSeverity SeverityOf(DecisionRecordKind kind) => kind switch
    {
        DecisionRecordKind.RuleExcluded or DecisionRecordKind.RuleFallback => DecisionSeverity.Warning,
        DecisionRecordKind.ToolCallMissing or DecisionRecordKind.DirectorParseFailed
            or DecisionRecordKind.ToolCallError or DecisionRecordKind.DecisionAborted => DecisionSeverity.Error,
        _ => DecisionSeverity.Info,
    };
}