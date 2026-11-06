#nullable enable

using System;
using System.Collections.Generic;

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

/// <summary>能力条目（三态 + 接受值域集）：域名由调用方给出（内核不认识领域词）。</summary>
/// <param name="State">三态。</param>
/// <param name="Domains">接受的值域名集合（<see cref="AnyDomain"/> = 任意）。</param>
public readonly record struct CapabilitySpec(CapabilityState State, IReadOnlySet<string> Domains)
{
    /// <summary>任意值域（不限制）。</summary>
    public const string AnyDomain = "*";

    /// <summary>未声明能力（等价 unsupported）。</summary>
    public static CapabilitySpec Unsupported { get; } =
        new(CapabilityState.Unsupported, new HashSet<string>(StringComparer.Ordinal));

    /// <summary>是否可投影（emulated 也可投影，仅记账）。</summary>
    public bool Supported => State != CapabilityState.Unsupported;

    /// <summary>是否声明了受限值域（不含 <see cref="AnyDomain"/>；受限 ⇒ 无法分类即越界）。</summary>
    public bool IsRestricted => !Domains.Contains(AnyDomain);

    /// <summary>是否接受该值域（空/未知域 ⇒ 不参与限制）。</summary>
    /// <param name="domain">值域名（调用方分类结果；null/空 = 不检查）。</param>
    public bool Accepts(string? domain)
        => Supported
           && (string.IsNullOrEmpty(domain)
               || Domains.Contains(AnyDomain)
               || Domains.Contains(domain));
}

/// <summary>能力三态的文本单源（声明面 → 枚举）。</summary>
public static class CapabilityStates
{
    /// <summary>原生支持词（任意值域）。</summary>
    public const string Supported = "supported";

    /// <summary>原生支持但只接受不透明值（对应「属性通道不能承载 alpha」）。</summary>
    public const string SupportedOpaque = "supported-opaque";

    /// <summary>可模拟词。</summary>
    public const string Emulated = "emulated";

    /// <summary>不支持词（未声明亦视为本值）。</summary>
    public const string Unsupported = "unsupported";

    /// <summary>值域词：不透明色。</summary>
    public const string DomainOpaque = "color";

    /// <summary>值域词：含 alpha 的色。</summary>
    public const string DomainAlpha = "color-alpha";

    /// <summary>文本 → 能力三态（未知/空 ⇒ Unsupported，可见降级；实现复用 <see cref="ParseSpec"/>）。</summary>
    /// <param name="value">声明面词。</param>
    public static CapabilityState Parse(string? value) => ParseSpec(value).State;

    /// <summary>文本 → 能力条目（三态 + 接受值域集）。</summary>
    /// <param name="value">声明面词。</param>
    public static CapabilitySpec ParseSpec(string? value)
        => value switch
        {
            SupportedOpaque => new(
                CapabilityState.Supported,
                new HashSet<string>(StringComparer.Ordinal) { DomainOpaque }),
            Supported => new(
                CapabilityState.Supported,
                new HashSet<string>(StringComparer.Ordinal) { CapabilitySpec.AnyDomain }),
            Emulated => new(
                CapabilityState.Emulated,
                new HashSet<string>(StringComparer.Ordinal) { CapabilitySpec.AnyDomain }),
            _ => CapabilitySpec.Unsupported,
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

