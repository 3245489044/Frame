namespace Share.ScopeFramework;

/// <summary>诊断严重级别（结构化过滤/聚合/对齐，替代裸字符串前缀约定）。</summary>
public enum DiagnosticKind
{
    /// <summary>信息（非成败）。</summary>
    Info,

    /// <summary>成功产出/执行。</summary>
    Ok,

    /// <summary>前置条件不满足/子项短路跳过。</summary>
    Skip,

    /// <summary>警告（可继续）。</summary>
    Warn,

    /// <summary>错误（阻断）。</summary>
    Error,
}
