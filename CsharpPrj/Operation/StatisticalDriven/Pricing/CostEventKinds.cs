namespace Operation.StatisticalDriven.Pricing;

/// <summary>
/// 成本引擎依赖的事件种类与指标键常量（单一来源，供采集端与引擎端对齐）。
/// 与观察契约 json 保持一致：LLM 沿用既有 <c>LlmCallCompleted</c>（token_in/token_out），
/// 计算/生命周期为新扩展事件族。
/// </summary>
public static class CostEventKinds
{
    // —— 事件种类 ——
    /// <summary>LLM 调用完成（既有，遵守 observability_semantics.json）。</summary>
    public const string LlmCallCompleted = "LlmCallCompleted";

    /// <summary>计算 Span 开始（与 ComputeSpanCompleted 配对，经 Activity 自动补全）。</summary>
    public const string ComputeSpanStarted = "ComputeSpanStarted";

    /// <summary>计算 Span 完成（进程树资源采样落点）。</summary>
    public const string ComputeSpanCompleted = "ComputeSpanCompleted";

    /// <summary>某轮请求的上下文构成快照（结构化构成入 PayloadJson）。</summary>
    public const string ContextSnapshot = "ContextSnapshot";

    /// <summary>本地输出首次进入上下文。</summary>
    public const string OutputEmitted = "OutputEmitted";

    /// <summary>上下文中的某段历史输出被再次重发进请求。</summary>
    public const string OutputReinjected = "OutputReinjected";

    // —— 实体类型 ——
    public const string EntityCompute = "Compute";
    public const string EntityContext = "Context";
    public const string EntityOutput = "Output";

    // —— LLM 指标键 ——
    public const string TokenIn = "token_in";
    public const string TokenOut = "token_out";
    public const string TokenCached = "token_cached";
    public const string TokenCacheWrite = "token_cache_write";
    public const string ContextLength = "context_length";

    // —— 计算资源指标键 ——
    public const string CpuUserSeconds = "cpu_user_seconds";
    public const string CpuKernelSeconds = "cpu_kernel_seconds";
    public const string MemoryGbSeconds = "memory_gb_seconds";
    public const string IoReadBytes = "io_read_bytes";
    public const string IoWriteBytes = "io_write_bytes";
    public const string OutputBytes = "output_bytes";
    public const string OutputTokensEstimated = "output_tokens_estimated";
    public const string ExitCode = "exit_code";

    // —— 输出生命周期指标键 ——
    /// <summary>本次重发注入的 token 数。</summary>
    public const string ReinjectedTokens = "reinjected_tokens";
}