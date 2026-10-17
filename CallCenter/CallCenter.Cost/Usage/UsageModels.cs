namespace CallCenter.Cost.Usage;

/// <summary>供应商 prompt 字段的缓存包含语义（通用性核心）。</summary>
public enum UsageShape
{
    /// <summary>prompt 总量已包含缓存命中（OpenAI/Gemini）：UncachedInput = prompt - cached。</summary>
    InclusiveCached,

    /// <summary>输入字段不含缓存读取，需相加（Anthropic/DeepSeek）：UncachedInput = input。</summary>
    ExclusiveCached,

    /// <summary>字段缺失或语义未知：不做推断，整单 Source=Unknown。</summary>
    Unknown,
}

/// <summary>用量来源可信度（真实遥测优先）。</summary>
public enum UsageSource
{
    ProviderReported,
    Reconciled,
    Estimated,
    Unknown,
}

/// <summary>
/// 归一后的 LLM 用量（供应商无关的唯一中间表示）。
/// 所有计量字段可空：缺遥测一律 null（Unknown），禁静默写 0。
/// </summary>
public sealed record LlmUsage(
    long? UncachedInput,
    long? CacheRead,
    long? CacheWrite,
    long? Output,
    UsageShape Shape,
    UsageSource Source)
{
    /// <summary>归一后 prompt 口径（缓存利用率与阶梯价的分母）。</summary>
    public long? PromptTotal => UncachedInput is null || CacheRead is null
        ? null
        : UncachedInput.Value + CacheRead.Value;

    /// <summary>归一后总 token。</summary>
    public long? Total => PromptTotal is null || Output is null
        ? null
        : PromptTotal.Value + Output.Value;

    /// <summary>是否具备可计价的最小真实遥测（输入与输出均已知）。</summary>
    public bool IsKnown => Source != UsageSource.Unknown
        && UncachedInput.HasValue
        && CacheRead.HasValue
        && Output.HasValue;

    /// <summary>未知用量单例（Source/Shape 均 Unknown）。</summary>
    public static LlmUsage UnknownUsage { get; } =
        new(null, null, null, null, UsageShape.Unknown, UsageSource.Unknown);

    /// <summary>以供应商上报口径构造（ProviderReported）。</summary>
    public static LlmUsage ProviderReported(
        long? uncachedInput, long? cacheRead, long? cacheWrite, long? output, UsageShape shape)
        => new(uncachedInput, cacheRead, cacheWrite, output, shape, UsageSource.ProviderReported);
}
