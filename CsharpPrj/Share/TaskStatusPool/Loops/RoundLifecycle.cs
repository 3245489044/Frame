using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Share.TaskStatusPool.Loops;

/// <summary>
/// 动作执行结果（引擎侧产物，含可选错误分类）。通用任务循环 canonical。
/// </summary>
public sealed class AxiomActionResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public ErrorClass? ErrorClass { get; init; }
    public string Text { get; init; } = "";
    public Dictionary<string, object> Outputs { get; init; } = new();
}

/// <summary>单轮执行产物。通用任务循环 canonical。</summary>
public sealed class AxiomRound
{
    public int Round { get; init; }
    public AxiomActionResult Result { get; init; } = new();
    public EvalOutcome Eval { get; init; }
    public ErrorClass? ErrorClass { get; init; }
    public LoopDecision Decision { get; init; }
    public string? NextHint { get; init; }
}

/// <summary>
/// 轮次生命周期五步（clinerules AX.RoundLifecycle）：
/// ①Execute → ②Collect → ③Evaluate → ④Decide → ⑤Adjust。
/// 演化约束单调性由引擎保证（每轮 token/budget 只按单一入口更新）。
/// 通用任务循环 canonical（SingleImplementation / TH-12），消费方（Agent 等使用层）仅调用，不重复实现。
/// </summary>
public static class RoundLifecycle
{
    public static async Task<AxiomRound> RunAsync(
        ILoopState ctx,
        Func<ILoopState, CancellationToken, Task<AxiomActionResult>> execute,
        Func<AxiomActionResult, ErrorClass?, EvalOutcome>? evaluate,
        CancellationToken ct = default)
    {
        // ① Execute
        var result = await execute(ctx, ct).ConfigureAwait(false);

        // ② Collect：归一错误分类（显式优先，否则按文本分类）
        var errCls = result.ErrorClass
            ?? (result.Error is { } e && e.Length > 0 ? ErrorClassifier.ClassifyError(e) : (ErrorClass?)null);
        ctx.LastResultText = result.Text;

        // ③ Evaluate：进度 + 熔断分别走单一入口
        var eval = evaluate != null ? evaluate(result, errCls) : InferEval(result, errCls);
        ProgressUpdater.Apply(ctx, eval);
        BudgetEscalator.Apply(ctx, errCls, eval);

        // 熔断落地（FailureBudgetEscalation level=3 → halt）：budget 达上限即置 halt，
        // 使 TerminationDecider 提前终止，避免在 MaxProgress > 3 时继续烧空 token 才停。
        // 默认 MaxProgress=3 时 token 与 budget 同步耗尽，本分支行为不变（幂等，irrecoverable 已由 BudgetEscalator 置位）。
        if (ctx.Budget.IsTripped)
            ctx.Flag.SetHalt();

        // ④ Decide：终止优先（halt/token 耗尽）；否则成功即 TS，未成功则继续
        LoopDecision decision;
        if (TerminationDecider.ShouldTerminate(ctx, out var term))
        {
            decision = term;
        }
        else if (result.Ok && eval == EvalOutcome.Pass)
        {
            decision = LoopDecision.Success; // TS：SUCCESS_CRITERIA(pass ∧ n≥1) 达成
        }
        else
        {
            decision = LoopDecision.Continue;
        }

        // ⑤ Adjust：推进轮次号，生成下一步提示（默认无提示）
        ctx.Round++;
        var hint = decision == LoopDecision.Success ? null : BuildNextHint(ctx, eval);

        return new AxiomRound
        {
            Round = ctx.Round,
            Result = result,
            Eval = eval,
            ErrorClass = errCls,
            Decision = decision,
            NextHint = hint,
        };
    }

    private static EvalOutcome InferEval(AxiomActionResult result, ErrorClass? errCls)
    {
        if (!result.Ok)
            return errCls == ErrorClass.ParseError ? EvalOutcome.ParseError : EvalOutcome.Fail;
        return string.IsNullOrWhiteSpace(result.Text) ? EvalOutcome.Partial : EvalOutcome.Pass;
    }

    private static string? BuildNextHint(ILoopState ctx, EvalOutcome eval)
        => eval switch
        {
            EvalOutcome.Pass => "上一步成功，继续下一步",
            EvalOutcome.Partial => "部分可用：基于上一步产出继续",
            _ => $"上一步未成功（预算 level={ctx.Budget.Level}, token={ctx.Token.Value}/{ctx.Token.Max}），修正后重试",
        };
}
