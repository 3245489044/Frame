#nullable enable

using System;
using System.Collections.Generic;
using Strategy.Refresh.Ports;

namespace FrontEndDriven.HtmlHosting;

/// <summary>
/// 快照指纹规格（通用件）—— 领域**声明一次**「哪些字段进入指纹、稳定/易变各取哪些」，
/// 由本类统一分隔符与哈希口径；不再手写 <see cref="System.Text.StringBuilder"/> 拼接。
/// <para>
/// 稳定/易变两层共用同一套声明机制：新增字段只改声明，避免「新增字段忘入指纹 ⇒ UI 陈旧」。
/// </para>
/// </summary>
/// <typeparam name="T">快照类型（引用类型）。</typeparam>
public sealed class FingerprintSpec<T> where T : class
{
    private readonly List<Action<T, FingerprintBuilder>> _steps = new();

    /// <summary>追加文本字段（null 记空段）。<paramref name="separator"/> 缺省为段内分隔。</summary>
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
    public string Of(T value, IFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(fingerprint);
        var builder = FingerprintBuilder.Using(fingerprint, 1024);
        foreach (var step in _steps) step(value, builder);
        return builder.Build();
    }
}
