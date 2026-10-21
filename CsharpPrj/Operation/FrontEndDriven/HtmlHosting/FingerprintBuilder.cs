#nullable enable

using System;
using System.Text;
using Strategy.Refresh.Ports;

namespace FrontEndDriven.HtmlHosting;

/// <summary>
/// 稳定指纹构建器（通用件）—— 按序追加字段并以不可歧义分隔符结束，最后一次哈希。
/// <para>领域只声明「哪些字段进入指纹」，不再手写 StringBuilder 拼接与分隔符口径。</para>
/// </summary>
public sealed class FingerprintBuilder
{
    private readonly StringBuilder _sb;
    private readonly Func<string, string> _hash;

    /// <summary>以给定哈希函数构造（如 <c>Fnv1aFingerprint.Default.Of</c>）。</summary>
    public FingerprintBuilder(Func<string, string> hash, int capacity = 256)
    {
        _hash = hash ?? throw new ArgumentNullException(nameof(hash));
        _sb = new StringBuilder(capacity);
    }

    /// <summary>以刷新框架指纹端口构造。</summary>
    public static FingerprintBuilder Using(IFingerprint fingerprint, int capacity = 256)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        return new FingerprintBuilder(fingerprint.Of, capacity);
    }

    /// <summary>追加文本字段（null 记为普通空段）。</summary>
    public FingerprintBuilder Append(string? value, char separator = '\u0001')
    {
        _sb.Append(value ?? string.Empty).Append(separator);
        return this;
    }

    /// <summary>追加整数字段。</summary>
    public FingerprintBuilder Append(int value, char separator = '\u0001')
    {
        _sb.Append(value).Append(separator);
        return this;
    }

    /// <summary>追加可空长整数字段。</summary>
    public FingerprintBuilder Append(long? value, char separator = '\u0001')
    {
        _sb.Append(value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).Append(separator);
        return this;
    }

    /// <summary>追加布尔字段。</summary>
    public FingerprintBuilder Append(bool value, char separator = '\u0001')
    {
        _sb.Append(value).Append(separator);
        return this;
    }

    /// <summary>原样追加（调用方自行保证分隔，用于嵌套片段拼接）。</summary>
    public FingerprintBuilder Raw(string? text)
    {
        _sb.Append(text ?? string.Empty);
        return this;
    }

    /// <summary>产出稳定哈希。</summary>
    public string Build() => _hash(_sb.ToString());
}
