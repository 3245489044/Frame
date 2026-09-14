namespace Operation.StatisticalDriven.Usage;

/// <summary>
/// 多供应商 usage 统一标准化字段（设计文档 5.1：input/output/cached/cache_write/total）。
/// 由各供应商 normalizer 从原始 usage JSON 映射得到。
/// </summary>
public sealed record UnifiedUsage(
    string Provider,
    string Model,
    long InputTokens,
    long OutputTokens,
    long CachedTokens,
    long CacheWriteTokens)
{
    public long TotalTokens => InputTokens + OutputTokens;
}