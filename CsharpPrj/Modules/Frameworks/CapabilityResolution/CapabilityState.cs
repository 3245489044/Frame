#nullable enable

using System;

namespace Modules.CapabilityResolution;

/// <summary>能力三态（唯一受控词）：不支持 / 模拟 / 原生支持。</summary>
public enum CapabilityState
{
    /// <summary>不支持（层必须降级或可见剔除）。</summary>
    Unsupported = 0,

    /// <summary>可模拟（保留层，但进 Issues 记账）。</summary>
    Emulated = 1,

    /// <summary>原生支持。</summary>
    Supported = 2,
}

/// <summary>能力三态的文本单源（声明面 → 枚举）。</summary>
public static class CapabilityStates
{
    /// <summary>原生支持词。</summary>
    public const string Supported = "supported";

    /// <summary>可模拟词。</summary>
    public const string Emulated = "emulated";

    /// <summary>不支持词（未声明亦视为本值）。</summary>
    public const string Unsupported = "unsupported";

    /// <summary>文本 → 三态（未知/空 ⇒ Unsupported，可见降级）。</summary>
    /// <param name="value">声明面词。</param>
    public static CapabilityState Parse(string? value)
        => value switch
        {
            Supported => CapabilityState.Supported,
            Emulated => CapabilityState.Emulated,
            _ => CapabilityState.Unsupported,
        };

    /// <summary>三态 → 文本（签名/诊断单源）。</summary>
    /// <param name="state">三态。</param>
    public static string Format(CapabilityState state)
        => state switch
        {
            CapabilityState.Supported => Supported,
            CapabilityState.Emulated => Emulated,
            _ => Unsupported,
        };
}
