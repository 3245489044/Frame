using System;
using System.Collections.Generic;
using System.Linq;

namespace Modules.Parsing.Invocation;

/// <summary>
/// 判据种类（唯一判据面）：只允许对探针的「状态 / 明细」断言；
/// 探针回显（EchoFields）不在类型面内 ⇒ 「期望词命中输入回显」的恒真判据不可表达。
/// </summary>
public enum CriterionKind
{
    DetailContains,
    DetailNotContains,
    StatusEquals,
    StatusIn,
}

/// <summary>一条判据（数据而非分支）：由声明面/命令层构造，判定函数唯一。</summary>
public sealed record Criterion(CriterionKind Kind, IReadOnlyList<string> Values)
{
    public static Criterion Contains(string text) => new(CriterionKind.DetailContains, new[] { text });

    public static Criterion ContainsAny(IReadOnlyList<string> tokens) => new(CriterionKind.DetailContains, tokens);

    public static Criterion StatusEquals(string status) => new(CriterionKind.StatusEquals, new[] { status });

    public static Criterion StatusIn(params string[] statuses) => new(CriterionKind.StatusIn, statuses);

    /// <summary>判定（纯函数）：None/空文本/空词表 ⇒ false（不静默通过）。</summary>
    public bool Matches(string? text)
    {
        if (Values is null || Values.Count == 0) return false;
        if (string.IsNullOrWhiteSpace(text)) return false;

        return Kind switch
        {
            CriterionKind.DetailNotContains => !AnyContains(text, Values),
            CriterionKind.StatusEquals => Values.Any(v => string.Equals(text.Trim(), v, StringComparison.OrdinalIgnoreCase)),
            CriterionKind.StatusIn => Values.Any(v => string.Equals(text.Trim(), v, StringComparison.OrdinalIgnoreCase)),
            _ => AnyContains(text, Values),
        };
    }

    /// <summary>文本是否命中任一词（大小写不敏感）；空词表恒假。</summary>
    public static bool AnyContains(string? text, IReadOnlyList<string> tokens)
    {
        if (string.IsNullOrWhiteSpace(text) || tokens is null || tokens.Count == 0) return false;

        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token)) continue;
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}

/// <summary>
/// 恒真判据守卫（结构事实判据）：期望词若等于探针名或任一探针入参值，则探针必然回显它，
/// `expectedContains` 会 0 轮命中 ⇒ 不是判据，是回显。命令层据此拒绝，不进入轮询。
/// </summary>
public static class ProbeEchoGuard
{
    public static bool IsTautological(
        string? expected,
        string? probe,
        IReadOnlyDictionary<string, object?>? args)
    {
        if (string.IsNullOrWhiteSpace(expected)) return false;
        var target = expected.Trim();

        if (!string.IsNullOrWhiteSpace(probe)
            && string.Equals(target, probe.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        if (args is null) return false;

        foreach (var pair in args)
        {
            if (pair.Value is null) continue;
            var text = pair.Value.ToString();
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (string.Equals(target, text.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}