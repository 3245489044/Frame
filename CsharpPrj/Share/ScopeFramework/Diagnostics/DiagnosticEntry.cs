namespace Share.ScopeFramework;

/// <summary>
/// 结构化诊断条目：步骤 + 级别 + 消息（替代裸字符串，支持按级别/步骤结构化过滤、聚合与对齐）。权威：设计 §4.3 诊断契约。
/// </summary>
public sealed record DiagnosticEntry(IStep Step, DiagnosticKind Kind, string Message)
{
    /// <summary>格式化回传统文本：<c>[kind] stepId: message</c>（兼容既有字符串诊断消费方）。</summary>
    public string Format()
        => $"[{Kind.ToString().ToLowerInvariant()}] {Step.Id}: {Message}";
}
