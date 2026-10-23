using System;
using System.Collections.Generic;
using System.Linq;
using Operation.HierarchyMethod.Nodes.Workflows;
using Share.TaskStatusPool.Loops;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 终态策略（成功判据单点）：优先级固定且短路 ——
/// T0 取消（由调用方上抛，不入终态）→ T1 执行失败（原样保留，不再判 E1/E2/E3）→
/// T2 端口契约 → T3 步次闭合 → T4 成功判据。
/// </summary>
public static class RoundTerminalPolicy
{
    public static RoundTerminal Evaluate(
        RoundSpec spec,
        NodeRoundResult result,
        NodeRoundContext ctx,
        Func<string?, LoopDecision>? decisionMapper = null,
        RoundContractVerdict? verdict = null,
        RoundTerminalPolicyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(ctx);

        var opts = options ?? RoundTerminalPolicyOptions.Default;
        var completed = CompletedSteps(ctx, result);
        var facts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["decision"] = result.Decision ?? string.Empty,
            ["completedCount"] = completed.Count.ToString(),
            ["duplicatePersistRegistrations"] = ctx.DuplicatePersistRegistrations.ToString(),
        };
        if (opts.LegacyOutcomeCompatibility)
            facts["legacyOutcome"] = result.Success ? "Produced" : "Failed";

        // T1：执行失败（强类型失败优先；退化为 Error 文本）。
        var executionFailure = ctx.Failure ?? result.Terminal?.Failure;
        if (executionFailure is null && !string.IsNullOrWhiteSpace(ctx.Error))
        {
            executionFailure = RoundFailure.ExecutionFailed(
                ctx.FailedStep ?? ctx.ErrorStep ?? FailedStepName(result, spec),
                completed,
                ctx.Error!);
        }

        if (executionFailure is null)
        {
            var bad = result.Records.FirstOrDefault(static r => !r.Ok);
            if (bad is not null)
                executionFailure = RoundFailure.ExecutionFailed(
                    bad.NodeKey, completed, bad.Error ?? "步骤执行失败");
        }

        if (executionFailure is not null)
        {
            facts["stage"] = "execution";
            return new RoundTerminal(RoundTerminalKind.Failed, result.Decision, executionFailure, facts);
        }

        var v = verdict ?? RoundContractJudge.Judge(
            spec, result.Records, completed, result.SkippedSteps,
            ctx.ContractOptions ?? RoundContractOptions.Default,
            judgeSteps: true);

        // T2：端口契约（成功路径）。
        if (v.MissingPorts.Count > 0)
        {
            facts["stage"] = "port";
            var step = StepOf(v.MissingPorts[0]);
            return new RoundTerminal(RoundTerminalKind.Failed, result.Decision,
                RoundFailure.PortMissing(step, completed, ProducedKeys(result),
                    $"声明端口未产出: [{string.Join(",", v.MissingPorts)}]"), facts);
        }

        // E2/E3：Report 档只可见化（迁移期不误杀存量流）；Throw 档由生命周期装饰器先行抛出。
        if (opts.Strictness == RoundContractStrictness.Throw &&
            (v.UndeclaredInports.Count > 0 || v.UndeclaredOutports.Count > 0))
        {
            var code = v.UndeclaredInports.Count > 0
                ? RoundFailureCodes.ExecUndeclaredInport
                : RoundFailureCodes.ExecUndeclaredOutport;
            var keys = v.UndeclaredInports.Count > 0 ? v.UndeclaredInports : v.UndeclaredOutports;
            facts["stage"] = "port";
            return new RoundTerminal(RoundTerminalKind.Failed, result.Decision,
                new RoundFailure(code, StepOf(keys[0]), completed, ProducedKeys(result),
                    $"端口契约未闭合: [{string.Join(";", keys)}]", keys), facts);
        }

        // T3：步次闭合（只允许在成功轮出现）。
        if (v.MissingSteps.Count > 0)
        {
            facts["stage"] = "chain";
            return new RoundTerminal(RoundTerminalKind.Failed, result.Decision,
                RoundFailure.StepMissing(v.MissingSteps[0], completed), facts);
        }

        // T4：成功判据（决策词→强类型映射已由后端裁决，此处不再二次映射）。
        _ = decisionMapper;
        if (!result.Success)
        {
            facts["stage"] = "criterion";
            return new RoundTerminal(RoundTerminalKind.Failed, result.Decision,
                RoundFailure.CriterionUnmet(FailedStepName(result, spec), completed,
                    $"成功判据未达成（criterion={spec.SuccessCriterion}）"), facts);
        }

        facts["stage"] = "success";
        return new RoundTerminal(RoundTerminalKind.Succeeded, result.Decision, null, facts);
    }

    /// <summary>
    /// 非步骤链内核（信号驱动折叠等）的终态构造**唯一入口**：内核只提供「成功/失败 + 失败载荷」这一结构事实，
    /// 终态类型与 <see cref="RoundTerminal"/> 构造不得由各内核内联（D1：终态构造点单源）。
    /// </summary>
    /// <param name="succeeded">内核信号（链收敛成功且无错误信封）。</param>
    /// <param name="decision">原始决策词（可空）。</param>
    /// <param name="failure">失败载荷（succeeded=true 时应为 null）。</param>
    /// <param name="facts">结构事实（键=事实名，值=结构事实）。</param>
    public static RoundTerminal FromSignal(
        bool succeeded,
        string? decision,
        RoundFailure? failure,
        IReadOnlyDictionary<string, string>? facts = null)
        => new(
            succeeded ? RoundTerminalKind.Succeeded : RoundTerminalKind.Failed,
            decision,
            succeeded ? null : failure,
            facts ?? new Dictionary<string, string>(StringComparer.Ordinal));

    private static IReadOnlyList<string> CompletedSteps(NodeRoundContext ctx, NodeRoundResult result)
        => ctx.ExecutedSteps
            .Concat(result.Records.Select(static r => r.NodeKey))
            .Where(static s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ProducedKeys(NodeRoundResult result)
        => result.Records.SelectMany(static r => r.OutputPorts.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static k => k, StringComparer.Ordinal)
            .ToArray();

    private static string FailedStepName(NodeRoundResult result, RoundSpec spec)
        => result.Records.Count > 0
            ? result.Records[^1].NodeKey
            : spec.Steps.Count > 0 ? spec.Steps[0].EffectiveNodeId : spec.Name;

    /// <summary>违规文本形如 <c>step:[key1,key2]</c>（取 step 段作失败步骤）。</summary>
    private static string StepOf(string violation)
    {
        var idx = violation.IndexOf(':', StringComparison.Ordinal);
        return idx > 0 ? violation[..idx] : violation;
    }
}
