#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace UiBackend.Html;

/// <summary>单条脚本命令的失败事实（判据载体=命令结果分型 + 证据串，不是执行者叙述）。</summary>
public sealed record HtmlScriptFailure(int Index, string Op, string Target, ClickOutcome Outcome, string Detail)
{
    public string ToEvidenceLine()
        => $"#{Index} op={Op} target={Target} outcome={ClickOutcomes.Prefix(Outcome)} detail={Detail}";
}

/// <summary>
/// 脚本判定失败（命令级）——把「失败只是返回值里的文本」升级为函数终态：
/// 宿主函数抛出后由 InvocationKernel 归因 FunctionFault，exec 命令随即判红，不再依赖脚本文案子串。
/// </summary>
public sealed class HtmlScriptFailedException : Exception
{
    public IReadOnlyList<HtmlScriptFailure> Failures { get; }

    public int UnsupportedCount { get; }

    public HtmlScriptFailedException(IReadOnlyList<HtmlScriptFailure> failures, int unsupportedCount)
        : base(BuildMessage(failures, unsupportedCount))
    {
        Failures = failures;
        UnsupportedCount = unsupportedCount;
    }

    public HtmlScriptFailedException(HtmlScriptResult result)
        : this(result.FailureList, result.UnsupportedCount)
    {
    }

    private static string BuildMessage(IReadOnlyList<HtmlScriptFailure> failures, int unsupportedCount)
    {
        var head = $"HTML 脚本判定失败: failedCount={failures.Count} unsupported={unsupportedCount}";
        return failures.Count == 0
            ? head
            : head + " | " + string.Join(" | ", failures.Select(f => f.ToEvidenceLine()));
    }
}
