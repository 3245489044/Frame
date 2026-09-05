namespace Share.ScopeFramework;

/// <summary>
/// enqueue 调度默认值（单一源）：任务入队未显式声明调度模式时默认 Deterministic（零 LLM 纯簿记），
/// 对齐 TaskPoolEnqueueAsync 权威默认（D1）。框架 S7 与 Agent 执行侧共享此常量，消除三处重复字面量。
/// </summary>
public static class ScopeEnqueueDefaults
{
    /// <summary>入队未显式调度模式时的默认调度模式（零 LLM 纯簿记，不默认触发 LLM）。</summary>
    public const string DefaultSchedulingMode = "Deterministic";
}
