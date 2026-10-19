namespace Modules.KeySpace.Dispatch;

/// <summary>
/// 分发阶段追踪（Observe 段结构化载体）：每阶段一条，字段可机械复算。
/// <para>缺任一阶段 ⇒ 调用方显式产生 <c>trace_missing</c>（不静默）。</para>
/// </summary>
public sealed record DispatchTrace(
    string TraceId,
    string Phase,
    string Scope,
    int Raw,
    int Planned,
    IReadOnlyList<string> Targets,
    IReadOnlyDictionary<string, int> Dropped,
    PlanFingerprint? Fingerprint,
    string Verdict);

/// <summary>阶段/schema 单源：phase 词表封闭，未知 phase 不得发射。</summary>
public static class DispatchTraceSchema
{
    /// <summary>六段管线阶段词表（序 = 管线序）。</summary>
    public static readonly IReadOnlyList<string> Phases = new[]
    {
        "resolve", "normalize", "plan", "gate", "activate", "observe",
    };

    /// <summary>阶段是否已登记（发射前校验）。</summary>
    public static bool IsKnown(string? phase)
        => !string.IsNullOrWhiteSpace(phase) && Phases.Contains(phase, StringComparer.Ordinal);

    /// <summary>裁决词表（禁用自由文本）。</summary>
    public static readonly IReadOnlyList<string> Verdicts = new[]
    {
        "ok", "empty", "stagnation", "blocked",
    };
}
