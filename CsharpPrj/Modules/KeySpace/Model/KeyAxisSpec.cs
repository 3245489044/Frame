using System;
using System.Collections.Generic;
using Modules.Parsing.Keys;

namespace Modules.KeySpace.Model;

/// <summary>
/// 键轴约束声明（数据，不是代码）：形状 / 必填 / 词表。
/// <para>
/// 单源纪律：<see cref="Shape"/> 只作声明期一致性断言（须与模板段形状一致，冲突即装配失败）；
/// 解析期形状由模板正则保证，本类型只追加 <see cref="Required"/> 与 <see cref="AllowedValues"/> 校验。
/// </para>
/// </summary>
public sealed record KeyAxisSpec(
    string Name,
    KeyValueShape Shape = KeyValueShape.Text,
    bool Required = true,
    IReadOnlyList<string>? AllowedValues = null,
    IReadOnlyList<string>? DisallowInfix = null)
{
    /// <summary>是否带词表约束（词表 = 该轴合法值全集）。</summary>
    public bool HasAllowedValues => AllowedValues is { Count: > 0 };

    /// <summary>是否声明「禁用中缀」（轴上保留段由声明表达，禁域类字面量判断）。</summary>
    public bool HasDisallowedInfix => DisallowInfix is { Count: > 0 };

    /// <summary>值是否命中词表（无词表 ⇒ 恒真）。</summary>
    public bool Allows(string? value)
        => !HasAllowedValues || (value is not null && AllowedValues!.Contains(value, StringComparer.Ordinal));

    /// <summary>值是否含声明保留中缀（无声明 ⇒ 恒假）。</summary>
    public bool Disallows(string? value)
        => HasDisallowedInfix && value is not null &&
           DisallowInfix!.Any(infix => !string.IsNullOrEmpty(infix) &&
                                        value.Contains(infix, StringComparison.Ordinal));
}
