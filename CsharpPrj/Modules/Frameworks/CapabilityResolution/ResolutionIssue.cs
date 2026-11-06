#nullable enable

namespace Modules.CapabilityResolution;

/// <summary>解析期可见问题（缺失/不支持/模拟/回退；禁静默）。</summary>
/// <param name="Code">受控码（本文件 <see cref="ResolutionIssueCodes"/> 为单源）。</param>
/// <param name="Detail">定位（材质/层/图元/载体）。</param>
public sealed record ResolutionIssue(string Code, string Detail);

/// <summary>解析问题码（唯一词表）。</summary>
public static class ResolutionIssueCodes
{
    /// <summary>层名未声明。</summary>
    public const string LayerUnknown = "layer-unknown";

    /// <summary>载体不支持该图元（已剔除）。</summary>
    public const string Unsupported = "unsupported";

    /// <summary>载体以模拟方式支持该图元（保留并记账）。</summary>
    public const string Emulated = "emulated";

    /// <summary>材质已回退到 fallback。</summary>
    public const string MaterialDegraded = "material-degraded";

    /// <summary>载体未声明能力（按不支持处理，可见）。</summary>
    public const string CarrierUndeclared = "carrier-undeclared";

    /// <summary>层值域超出该通道能力（如属性通道拿到 alpha 色；已剔除并降级）。</summary>
    public const string ValueOutOfDomain = "value-out-of-domain";
}
