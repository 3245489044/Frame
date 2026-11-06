#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Share.IdentityFramework;

/// <summary>
/// 声明式指纹构建器：按序追加字段并以不可歧义分隔符结束，最后一次哈希。
/// <para>调用方只声明"哪些字段进入指纹"，不再手写 StringBuilder 拼接与分隔符口径。</para>
/// </summary>
public sealed class FingerprintBuilder
{
    private readonly StringBuilder _sb;
    private readonly Func<string, string> _hash;

    /// <summary>以给定哈希函数构造（如 <see cref="Fnv1a64Fingerprint.Default"/> 的 <c>Of</c>）。</summary>
    public FingerprintBuilder(Func<string, string> hash, int capacity = 256)
    {
        _hash = hash ?? throw new ArgumentNullException(nameof(hash));
        _sb = new StringBuilder(capacity);
    }

    /// <summary>以指纹算法构造。</summary>
    public static FingerprintBuilder Using(IContentFingerprint fingerprint, int capacity = 256)
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
        _sb.Append(value.ToString(CultureInfo.InvariantCulture)).Append(separator);
        return this;
    }

    /// <summary>追加可空长整数字段。</summary>
    public FingerprintBuilder Append(long? value, char separator = '\u0001')
    {
        _sb.Append(value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(separator);
        return this;
    }

    /// <summary>追加布尔字段。</summary>
    public FingerprintBuilder Append(bool value, char separator = '\u0001')
    {
        _sb.Append(value ? '1' : '0').Append(separator);
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

/// <summary>
/// 快照指纹规格（通用件）：领域声明一次"哪些字段进入指纹、稳定/易变各取哪些"，
/// 由本类统一分隔符与哈希口径；新增字段只改声明，避免"新增字段忘入指纹 ⇒ UI 陈旧"。
/// </summary>
/// <typeparam name="T">快照类型（引用类型）。</typeparam>
public sealed class FingerprintSpec<T> where T : class
{
    private readonly List<Action<T, FingerprintBuilder>> _steps = new();

    /// <summary>追加文本字段（null 记空段）。</summary>
    public FingerprintSpec<T> Field(Func<T, string?> select, char separator = '\u0001')
    {
        ArgumentNullException.ThrowIfNull(select);
        _steps.Add((value, builder) => builder.Append(select(value), separator));
        return this;
    }

    /// <summary>追加整数字段。</summary>
    public FingerprintSpec<T> Number(Func<T, int> select, char separator = '\u0001')
    {
        ArgumentNullException.ThrowIfNull(select);
        _steps.Add((value, builder) => builder.Append(select(value), separator));
        return this;
    }

    /// <summary>追加布尔字段。</summary>
    public FingerprintSpec<T> Flag(Func<T, bool> select, char separator = '\u0001')
    {
        ArgumentNullException.ThrowIfNull(select);
        _steps.Add((value, builder) => builder.Append(select(value), separator));
        return this;
    }

    /// <summary>逐项追加序列（null/空 ⇒ 只留空段；每项以分隔符结束）。</summary>
    public FingerprintSpec<T> Each<TItem>(
        Func<T, IEnumerable<TItem>?> select, Func<TItem, string> format, char separator = '\u0001')
    {
        ArgumentNullException.ThrowIfNull(select);
        ArgumentNullException.ThrowIfNull(format);
        _steps.Add((value, builder) =>
        {
            if (select(value) is { } items)
            {
                foreach (var item in items) builder.Append(format(item), separator);
            }
        });
        return this;
    }

    /// <summary>嵌套段（复杂结构逃生口；调用方仍只追加、不自行哈希）。</summary>
    public FingerprintSpec<T> Group(Action<T, FingerprintBuilder> append)
    {
        ArgumentNullException.ThrowIfNull(append);
        _steps.Add(append);
        return this;
    }

    /// <summary>按声明顺序计算指纹（一次哈希）。</summary>
    public string Of(T value, IContentFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(fingerprint);
        var builder = FingerprintBuilder.Using(fingerprint, 1024);
        foreach (var step in _steps) step(value, builder);
        return builder.Build();
    }
}
