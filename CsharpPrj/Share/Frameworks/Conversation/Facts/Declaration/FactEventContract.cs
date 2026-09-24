using System;
using System.Collections.Generic;

namespace Share.Conversation.Facts.Declaration;

/// <summary>
/// 事实事件契约（<c>[Obs] kind=event</c> 的运行时投影）——<c>required</c> 缺一即**不产事实**（不假装产出）、
/// <c>enum</c> 越界即拒（禁自然语言错误串充当事实）、<c>metrics</c> 为度量键（成本 / 体积 / 推进的单一记分牌来源）。
/// </summary>
/// <param name="Name">事件名（= 线格式 kind 值；默认词表见 <see cref="FactKindNames"/>）。</param>
/// <param name="Entity">归属实体（<c>round_fact</c> / <c>step_fact</c>）。</param>
/// <param name="Required">必填字段（缺一即不产事实）。</param>
/// <param name="Metrics">度量字段。</param>
/// <param name="Enums">受控值域（字段 → 合法值表）。</param>
public sealed record FactEventContract(
    string Name,
    string? Entity,
    IReadOnlyList<string> Required,
    IReadOnlyList<string> Metrics,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Enums)
{
    /// <summary>缺失的必填字段（空表 = 契约满足；缺失 / 空白均计缺失）。</summary>
    /// <param name="fields">待校验字段值。</param>
    public IReadOnlyList<string> MissingFields(IReadOnlyDictionary<string, string?> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var missing = new List<string>();
        foreach (var key in Required)
        {
            if (!fields.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) missing.Add(key);
        }
        return missing;
    }

    /// <summary>字段值是否落在声明值域（该字段未声明枚举 ⇒ 恒真）。</summary>
    /// <param name="field">字段名。</param>
    /// <param name="value">字段值。</param>
    public bool IsAllowedValue(string field, string? value)
        => !Enums.TryGetValue(field, out var allowed)
            || allowed.Contains(value ?? string.Empty, StringComparer.Ordinal);

    /// <summary>是否声明了该受控字段。</summary>
    /// <param name="field">字段名。</param>
    public bool HasEnum(string field) => Enums.ContainsKey(field);
}
