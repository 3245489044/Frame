namespace Operation.HierarchyMethod.Abstractions;

/// <summary>
/// 框架诊断码常量（单源）—— 消除各调用点手写诊断字符串造成的口径漂移（门禁 / 检索对账面）。
/// </summary>
public static class DiagnosticsCodes
{
    /// <summary>簿记流入口未装配（执行器 / 端口缺失）：调用方须按「未完成」口径处置，禁当作已完成。</summary>
    public const string FlowMissing = "flow_entry_missing";

    /// <summary>簿记流执行失败（声明流抛错 / 步骤失败）。</summary>
    public const string FlowFailed = "flow_run_failed";

    /// <summary>簿记流入口缺失时代的**直调回退**已触发（迁移期计数：口径可见化，供门禁基线核减）。</summary>
    public const string FlowFallback = "flow_legacy_fallback";

    /// <summary>端口 / 依赖未装配（可空守卫）。</summary>
    public const string Unavailable = "port_unavailable";
}
