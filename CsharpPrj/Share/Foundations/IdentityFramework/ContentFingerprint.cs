#nullable enable

using System;
using System.Security.Cryptography;
using System.Text;

namespace Share.IdentityFramework;

/// <summary>
/// 内容指纹算法（单一职责）：把规范化文本映射为稳定十六进制串。
/// <para>只回答"同一文本得同一串"，不定义"什么算内容变化"、不决定版本推进。</para>
/// </summary>
public interface IContentFingerprint
{
    /// <summary>对规范化文本计算指纹。</summary>
    string Of(string canonical);
}

/// <summary>
/// FNV-1a 64 位指纹（UTF-8 字节，输出 16 位小写十六进制）。
/// <para>刻意不用 <c>string.GetHashCode</c>：后者随进程随机化，跨进程不稳定。</para>
/// <para>与 <c>Strategy.Refresh.Core.Fnv1aFingerprint</c> 的输出值完全一致，可原值迁移。</para>
/// </summary>
public sealed class Fnv1a64Fingerprint : IContentFingerprint
{
    /// <summary>无状态单源实例（进程内可安全共享）。</summary>
    public static readonly Fnv1a64Fingerprint Default = new();

    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    /// <inheritdoc />
    public string Of(string canonical)
    {
        var bytes = Encoding.UTF8.GetBytes(canonical ?? string.Empty);
        var hash = OffsetBasis;
        for (var i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= Prime;
        }

        return hash.ToString("x16");
    }
}

/// <summary>
/// SHA-256 前缀指纹（截断长度与大小写显式注入）。
/// <para>覆盖既有两种口径：<c>IPortInput.ArgsHash</c> 为 12 位大写，<c>CallLogWireLedger</c> 为 16 位小写。
/// 统一时只合并"算法"，不改变任一方的输出长度与大小写。</para>
/// </summary>
public sealed class Sha256PrefixFingerprint : IContentFingerprint
{
    private readonly int _hexChars;
    private readonly bool _upperCase;

    /// <param name="hexChars">保留的十六进制字符数（1..64）。</param>
    /// <param name="upperCase">true = 大写输出。</param>
    public Sha256PrefixFingerprint(int hexChars, bool upperCase = true)
    {
        if (hexChars is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(hexChars), "指纹截断长度必须在 1..64 个十六进制字符之间。");
        _hexChars = hexChars;
        _upperCase = upperCase;
    }

    /// <inheritdoc />
    public string Of(string canonical)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical ?? string.Empty));
        var hex = Convert.ToHexString(bytes);
        var prefix = hex[.._hexChars];
        return _upperCase ? prefix : prefix.ToLowerInvariant();
    }
}

/// <summary>
/// 指纹算法静态入口（无状态；仅做委托，不新增第三种算法）。
/// </summary>
public static class ContentDigests
{
    /// <summary>FNV-1a 64 位（16 位小写十六进制）。</summary>
    public static string Fnv1a64(string canonical) => Fnv1a64Fingerprint.Default.Of(canonical);

    /// <summary>SHA-256 前缀（默认 12 位大写，与端口实参指纹口径一致）。</summary>
    public static string Sha256Prefix(string canonical, int hexChars = 12, bool upperCase = true)
        => new Sha256PrefixFingerprint(hexChars, upperCase).Of(canonical);
}
