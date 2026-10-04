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
/// <para>
/// 分级复用纪律：每步各有一个 canonical 函数，供脊柱管道按作用点（<c>ActionStageId</c>）分级调用；
/// <see cref="RunAsync"/> 为五步组合（原子单轮，外部契约不变）。消费方**只调用**本级 canonical 函数，
/// 不得在宿主持有步内语义副本（分级 ≠ 分叉）。
/// </para>
/// </summary>
public static class RoundLifecycle
{
    /// <summary>① Execute —— 单轮执行体（宿主注入；返回本轮原始产物）。</summary>
    public static Task<AxiomActionResult> ExecuteAsync(
        ILoopState ctx,
        Func<ILoopState, CancellationToken, Task<AxiomActionResult>> execute,
        CancellationToken ct = default)
    {
        PortGuard.Required(execute, nameof(execute));
        return execute(ctx, ct);
    }

    /// <summary>
    /// ② Collect —— 归一错误分类（<paramref name="explicitErrorClass"/> 优先，其次产物自带，最后按错误文本分类）
    /// + 上一轮结果文本落状态。
    /// </summary>
    public static ErrorClass? Collect(ILoopState ctx, AxiomActionResult result, ErrorClass? explicitErrorClass = null)
    {
        PortGuard.Required(ctx, nameof(ctx));
        PortGuard.Required(result, nameof(result));

        var errCls = explicitErrorClass
            ?? result.ErrorClass
            ?? (result.Error is { } e && e.Length > 0 ? ErrorClassifier.ClassifyError(e) : (ErrorClass?)null);
        ctx.LastResultText = result.Text;
        return errCls;
    }

    /// <summary>③ Evaluate —— eval 求值（宿主回调优先，否则按执行结果推断）+ 进度/熔断单一入口更新 + 熔断落地。</summary>
    public static EvalOutcome Evaluate(
        ILoopState ctx,
        AxiomActionResult result,
        ErrorClass? errCls,
        Func<AxiomActionResult, ErrorClass?, EvalOutcome>? evaluate = null)
    {
        PortGuard.Required(ctx, nameof(ctx));
        PortGuard.Required(result, nameof(result));

        // 进度 + 熔断分别走单一入口
        var eval = evaluate != null ? evaluate(result, errCls) : InferEval(result, errCls);
        ProgressUpdater.Apply(ctx, eval);
        BudgetEscalator.Apply(ctx, errCls, eval);

        // 熔断落地（FailureBudgetEscalation level=3 → halt）：budget 达上限即置 halt，
        // 使 TerminationDecider 提前终止，避免在 MaxProgress > 3 时继续烧空 token 才停。
        // 默认 MaxProgress=3 时 token 与 budget 同步耗尽，本分支行为不变（幂等，irrecoverable 已由 BudgetEscalator 置位）。
        if (ctx.Budget.IsTripped)
            ctx.Flag.SetHalt();

        return eval;
    }

    /// <summary>④ Decide —— 终止优先（halt/token 耗尽）；否则成功即 TS，未成功则继续。</summary>
    public static LoopDecision Decide(ILoopState ctx, AxiomActionResult result, EvalOutcome eval)
    {
        PortGuard.Required(ctx, nameof(ctx));
        PortGuard.Required(result, nameof(result));

        if (TerminationDecider.ShouldTerminate(ctx, out var term))
            return term;

        // TS：SUCCESS_CRITERIA(pass ∧ n≥1) 达成
        return result.Ok && eval == EvalOutcome.Pass ? LoopDecision.Success : LoopDecision.Continue;
    }

    /// <summary>⑤ Adjust —— 推进轮次号，生成下一步提示（成功无提示）。</summary>
    public static string? Adjust(ILoopState ctx, LoopDecision decision, EvalOutcome eval)
    {
        PortGuard.Required(ctx, nameof(ctx));

        ctx.Round++;
        return decision == LoopDecision.Success ? null : BuildNextHint(ctx, eval);
    }

    /// <summary>五步组合（原子单轮）：①→⑤ 顺序不变，外部契约与既有实现等价。</summary>
    public static async Task<AxiomRound> RunAsync(
        ILoopState ctx,
        Func<ILoopState, CancellationToken, Task<AxiomActionResult>> execute,
        Func<AxiomActionResult, ErrorClass?, EvalOutcome>? evaluate,
        CancellationToken ct = default)
    {
        var result = await ExecuteAsync(ctx, execute, ct).ConfigureAwait(false);
        var errCls = Collect(ctx, result);
        var eval = Evaluate(ctx, result, errCls, evaluate);
        var decision = Decide(ctx, result, eval);
        var hint = Adjust(ctx, decision, eval);

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
