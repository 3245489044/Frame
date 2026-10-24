using System;
using System.Collections.Generic;
using System.Linq;
using Share.Composition.Text;
using Share.Conversation.ContextBinding;

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
    /// <summary>
    /// 解析前置条件（判据唯一实现 = <see cref="TextJudge"/>）：必填端口 → 契约 → 轻量计划 → 结论。
    /// <para>空白口径 = <see cref="BlankSemantics.Absent"/>（提示词域空白串视为未给出）；别名派生同一判据内完成。</para>
    /// <para>别名唯一权威 = 段声明面（<see cref="PromptSegmentAliases"/>）：本处不再自持第二张表。</para>
    /// </summary>
    /// <param name="ports">工作流参数空间（可空）。</param>
    /// <param name="required">声明面必填端口（空/未声明 ⇒ 直接 Resolved(<c>declaration-off</c>)）。</param>
    public static PromptPreflightResult Resolve(IReadOnlyDictionary<string, object?>? ports, IReadOnlyList<string>? required)
    {
        if (required is not { Count: > 0 })
            return PromptPreflightResult.Resolved(Array.Empty<string>(), "declaration-off");

        var contract = BuildContract(required);
        var context = new SegmentContext(ports);
        var plan = TextPlan.FromPorts(contract, context, BlankSemantics.Absent);
        var verdict = TextJudge.Judge(plan, context, contract, BlankSemantics.Absent);

        var missing = verdict.Missing
            .Select(static k => k.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static p => p, StringComparer.Ordinal)
            .ToArray();

        return missing.Length == 0
            ? PromptPreflightResult.Resolved(required, verdict.Derived.Count > 0 ? "derived" : "declared")
            : PromptPreflightResult.MissingPortsOf(required, missing, "缺少必填提示词端口：" + string.Join("/", missing));
    }

    /// <summary>必填端口 → 契约（需求量/可空/别名全部来自声明，判定里不再另写一套）。</summary>
    private static TextContract BuildContract(IReadOnlyList<string> required)
    {
        var ports = new List<PortSpec>(required.Count);
        foreach (var port in required)
        {
            var name = (port ?? string.Empty).Trim();
            if (name.Length == 0) continue;

            ports.Add(new PortSpec(
                new TextKey(name),
                SegmentRequirement.Required,
                AllowEmpty: false,
                Aliases: PromptSegmentAliases.Of(name),
                Applicable: static _ => true));
        }
        return new TextContract(ports);
    }
}
