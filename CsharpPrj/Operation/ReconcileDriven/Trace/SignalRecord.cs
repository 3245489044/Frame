namespace Operation.ReconcileDriven.Trace;

/// <summary>
/// 单条信号记录（触发域信号账元素）：语义观测事件的投影，字段全通用、无宿主语义。
/// </summary>
/// <param name="EventKind">事件分类（须在语义词表内）。</param>
/// <param name="EventSeq">ScopeId 内单调递增序号（因果序依据）。</param>
/// <param name="ScopeId">作用域标识（归属核对依据）。</param>
/// <param name="Phase">阶段（可选）。</param>
/// <param name="Stage">子阶段（可选）。</param>
/// <param name="TraceId">关联 TraceId（归属核对依据；来自 ambient 捕获）。</param>
/// <param name="PayloadJson">载荷 JSON 原文（可选；值层 payload 断言用，不作对账主干）。</param>
public sealed record SignalRecord(
    string EventKind,
    long EventSeq,
    string ScopeId,
    string? Phase,
    string? Stage,
    string? TraceId,
    string? PayloadJson);
