using System;
using System.Collections.Generic;
using System.Linq;
using Operation.HierarchyMethod.Judgment;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>提示词前置解析结论（I4）：<c>Resolved</c> / <c>Missing</c> 二态，禁静默。</summary>
public sealed record PromptPreflightResult(
    bool Ok,
    IReadOnlyList<string> RequiredPorts,
    IReadOnlyList<string> MissingPorts,
    string Source,
    string? Diagnostic = null)
{
    /// <summary>通过。</summary>
    public static PromptPreflightResult Resolved(IReadOnlyList<string> required, string source)
        => new(true, required, Array.Empty<string>(), source);

    /// <summary>缺失（<paramref name="missing"/> 非空）。</summary>
    public static PromptPreflightResult MissingPortsOf(
        IReadOnlyList<string> required, IReadOnlyList<string> missing, string? diagnostic = null)
        => new(false, required, missing, "missing", diagnostic);
}

/// <summary>
/// 提示词前置（I4 / P0-6，纯函数）：在任何执行体（含 daemon）启动前判定「本轮声明的必填提示词端口」是否成立。
/// <para>
/// 声明面 = <see cref="Workflows.RoundSpec.RequiredPromptPorts"/>（缺省空 ⇒ 不启用前置，零回归）；
/// 端口来源 = 工作流参数空间（<c>system</c> / <c>l1</c> / <c>l2</c> / <c>session_input</c> / <c>session_window</c>）。
/// </para>
/// <para>
/// 派生规则（R5）：声明端口缺失时，若存在可派生的同义端口（<c>session_input→l1</c>、<c>session_window→l2</c>）
/// 则判 Resolved 并记 <c>Source=derived</c>；不可派生 ⇒ <c>Missing</c>（<c>prompt_missing</c>），
/// 由上层拒绝启动 daemon / 归档写，禁以「补空串」蒙混。
/// </para>
/// </summary>
public static class PromptPreflight
{
    /// <summary>派生别名（声明口径单源）：声明端口 → 可替代来源（顺序即优先级）。</summary>
    private static readonly IReadOnlyDictionary<string, string[]> Aliases =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["l1"] = new[] { "session_input" },
            ["l2"] = new[] { "session_window" },
            ["session_input"] = new[] { "l1" },
            ["session_window"] = new[] { "l2" },
        };

    private static readonly IReadOnlyList<IJudgmentRule> Rules = new IJudgmentRule[]
    {
        new AliasCoverageRule(),
    };

    /// <summary>
    /// 解析前置条件（判据唯一实现 = <see cref="AliasCoverageRule"/>）。
    /// </summary>
    /// <param name="ports">工作流参数空间（可空）。</param>
    /// <param name="required">声明面必填端口（空/未声明 ⇒ 直接 Resolved(<c>declaration-off</c>)）。</param>
    public static PromptPreflightResult Resolve(IReadOnlyDictionary<string, object?>? ports, IReadOnlyList<string>? required)
    {
        if (required is not { Count: > 0 })
            return PromptPreflightResult.Resolved(Array.Empty<string>(), "declaration-off");

        var input = new JudgmentInput(JudgmentScope.Preflight)
        {
            DeclaredPorts = required.Where(static p => !string.IsNullOrWhiteSpace(p)).ToArray(),
            Fact = new JudgmentFact(ports ?? new Dictionary<string, object?>(StringComparer.Ordinal)),
            Capability = new DeclarationCapability(Aliases: Aliases),
            Semantics = PresenceSemantics.PromptPreflight,
        };

        var verdict = JudgmentEngine.Judge(input, Rules);
        var missing = verdict.Violations.SelectMany(static f => f.Subjects)
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static p => p, StringComparer.Ordinal)
            .ToArray();
        var derived = verdict.Findings.Any(static f => f.Code == AliasCoverageRule.DerivedCode);

        return missing.Length == 0
            ? PromptPreflightResult.Resolved(required, derived ? "derived" : "declared")
            : PromptPreflightResult.MissingPortsOf(required, missing, "缺少必填提示词端口：" + string.Join("/", missing));
    }
}
