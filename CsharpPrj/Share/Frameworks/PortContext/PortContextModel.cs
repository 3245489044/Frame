using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Share.PortContexts;

/// <summary>单条成员窗口片段（Owner = 成员键；Text = 窗口文本）。</summary>
public sealed record PortContextWindowItem(string Owner, string Text);

/// <summary>单个上下文字段（分类 + 稳定度 + 键值）。</summary>
public sealed record PortContextField(string Class, string Volatility, string Key, string? Value);

/// <summary>单成员上下文：成员键 + 状态字段 + 窗口片段 + 空因。</summary>
public sealed record PortContextMember(
    string MemberKey,
    IReadOnlyList<PortContextField> StateFields,
    IReadOnlyList<PortContextWindowItem> WindowItems,
    string? EmptyCode = null,
    bool Truncated = false);

/// <summary>
/// 一次装配的不可变结果：容器键 + 成员 + 合并字段 + 空因 + 合成标记 + 源指纹。
/// <para>只读派生、实时、有界；非第二真源（对齐 AX.StateRootSingleSource）。</para>
/// </summary>
public sealed record PortContext(
    string ContainerKey,
    IReadOnlyList<PortContextMember> Members,
    IReadOnlyList<PortContextField> Fields,
    string? EmptyCode,
    bool Synthetic,
    string SourceFingerprint,
    bool Truncated = false,
    string? TruncationCode = null)
{
    /// <summary>是否有成员事实。</summary>
    public bool HasMembers => Members.Count > 0;

    /// <summary>是否带空因（Present 且无空因 ⇒ false）。</summary>
    public bool IsEmpty => !string.IsNullOrWhiteSpace(EmptyCode);

    /// <summary>空结果（空因必显式；禁静默空）。</summary>
    /// <param name="containerKey">容器键。</param>
    /// <param name="code">空因码（受控词）。</param>
    /// <param name="synthetic">是否合成壳。</param>
    public static PortContext Empty(string containerKey, string code, bool synthetic = false)
        => new(containerKey, Array.Empty<PortContextMember>(), Array.Empty<PortContextField>(),
            code, synthetic, Fingerprint(containerKey, code, Array.Empty<string>()));

    /// <summary>把既存结果标记为合成壳（合成项不得清除空因）。</summary>
    /// <param name="context">既存结果。</param>
    /// <param name="code">合成原因码（缺省 synthetic-only）。</param>
    public static PortContext AsSynthetic(PortContext context, string? code = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var reason = !string.IsNullOrWhiteSpace(context.EmptyCode)
            ? context.EmptyCode
            : (string.IsNullOrWhiteSpace(code) ? PortContextEmptyCodes.SyntheticOnly : code);
        return context with { Synthetic = true, EmptyCode = reason };
    }

    /// <summary>源指纹（内容指纹；非 mtime/执行者叙述）。</summary>
    /// <param name="containerKey">容器键。</param>
    /// <param name="code">空因码（可空）。</param>
    /// <param name="memberKeys">成员键序。</param>
    public static string Fingerprint(string? containerKey, string? code, IReadOnlyList<string>? memberKeys)
    {
        var payload = new StringBuilder()
            .Append(containerKey ?? string.Empty).Append('\u0002')
            .Append(code ?? string.Empty).Append('\u0002');
        if (memberKeys is not null)
        {
            foreach (var key in memberKeys) payload.Append(key).Append('\u0003');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString()));
        return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }
}
