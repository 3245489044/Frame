namespace Share.Frameworks.Billing;

/// <summary>
/// 多供应商 usage 统一标准化字段（设计文档 5.1：input/output/cached/cache_write/total）。
/// 由各供应商 normalizer 从原始 usage JSON 映射得到。
/// </summary>
/// <param name="InputIncludesCached">
/// 原始 <see cref="InputTokens"/> 是否已包含 <see cref="CachedTokens"/>。
/// OpenAI/DeepSeek/Gemini 的 prompt_tokens 含缓存命中部分（true）；Anthropic 的 input_tokens 不含（false）。
/// 成本核算据此拆分互斥的 rawInput，禁止在公式里无条件做 input-cached。
/// </param>
public sealed record UnifiedUsage(
    string Provider,
    string Model,
    long InputTokens,
    long OutputTokens,
    long CachedTokens,
    long CacheWriteTokens,
    bool InputIncludesCached = true)
{
    /// <summary>总 token（输入 + 输出 + 缓存写入；缓存命中已含于输入，不重复相加）。</summary>
    public long TotalTokens => InputTokens + OutputTokens + CacheWriteTokens;
}
