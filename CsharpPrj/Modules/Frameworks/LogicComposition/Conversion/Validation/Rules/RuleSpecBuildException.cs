using System;
using System.Collections.Generic;

namespace Share.LogicComposition.Conversion.Validation.Rules;

/// <summary>
/// 规则表**构筑期**失败：规则数据本身非法（≠ 被校验文档非法），故**不走运行期诊断流**，直接抛。
/// <para>
/// 携带**全部**错误（非首个短路），保持「全量失败可见」；由守卫测试在 CI 拦截（对齐 RuleSpec §8）。
/// </para>
/// </summary>
public sealed class RuleSpecBuildException : Exception
{
    /// <summary>构筑期错误清单（声明序稳定）。</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>构筑失败（单条错误）。</summary>
    /// <param name="error">错误描述。</param>
    public RuleSpecBuildException(string error)
        : this(new[] { error ?? string.Empty })
    {
    }

    /// <summary>构筑失败（多条错误）。</summary>
    /// <param name="errors">错误清单。</param>
    public RuleSpecBuildException(IEnumerable<string> errors)
        : base(BuildMessage(errors))
    {
        var list = new List<string>(errors ?? Array.Empty<string>());
        Errors = list;
    }

    private static string BuildMessage(IEnumerable<string> errors)
    {
        var list = new List<string>(errors ?? Array.Empty<string>());
        if (list.Count == 0) return "规则表构筑失败";

        return "规则表构筑失败（" + list.Count + " 项）：" + string.Join("；", list);
    }
}
