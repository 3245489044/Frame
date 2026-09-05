namespace Share.TaskStatusPool.Loops;

/// <summary>
/// 进度维护唯一入口（clinerules formula SM.EvaluateDrivenUpdate）。
/// 依据 eval 更新 ProgressToken：pass → token-1；fail/partial/parse_error → token+1。
/// 本类不写 FailureBudget（其升级由 <see cref="BudgetEscalator"/> 单一裁决），二者职责分离。
/// </summary>
public static class ProgressUpdater
{
    public static void Apply(ILoopState ctx, EvalOutcome eval)
    {
        switch (eval)
        {
            case EvalOutcome.Pass:
                ctx.Token.Retreat();
                break;
            case EvalOutcome.Fail:
            case EvalOutcome.Partial:
            case EvalOutcome.ParseError:
                ctx.Token.Advance();
                break;
        }

        ctx.LastEval = eval;
    }
}

/// <summary>
/// 熔断升降级（clinerules AX.FailureBudgetEscalation）。
/// 裁决执行实例：error 计入（含 compilation_error）→ level 升级；eval=pass → 衰减；
/// irrecoverable → level=3 且 halt=1；partial_output 不升级（"部分可用"严重度低于格式违规）。
/// 本类不写 ProgressToken（其更新由 <see cref="ProgressUpdater"/> 单一入口负责）。
/// </summary>
public static class BudgetEscalator
{
    public static void Apply(ILoopState ctx, ErrorClass? error, EvalOutcome eval)
    {
        if (error == ErrorClass.Irrecoverable)
        {
            ctx.Budget.Trip();
            ctx.Flag.SetHalt();
            return;
        }

        bool escalated = false;
        switch (error)
        {
            case ErrorClass.ExecutionFailure:
            case ErrorClass.Timeout:
            case ErrorClass.CompilationError:
            case ErrorClass.EmptyResult:
            case ErrorClass.ResourceExhausted:
            case ErrorClass.ParseError:
                // 失败/格式违规统一计入熔断预算（含 compilation_error，无 continue 特权）
                ctx.Budget.Escalate();
                escalated = true;
                break;
            case ErrorClass.PartialOutput:
                // 部分可用 → 不升级
                break;
        }

        // 失败升级与成功衰减互斥：仅当本帧未计入失败时才按成功衰减（E5）
        if (!escalated && eval == EvalOutcome.Pass)
            ctx.Budget.Deescalate();
    }
}

/// <summary>
/// 终止裁决（clinerules AX.TerminationPriority）。
/// 终止判定仅两条：ProgressToken.value≥MAX_PROGRESS → TO/TM；ControlFlag.halt=1 → TM。
/// 废除旧 CF≥MAX_FAIL&gt;QDC&gt;CP&gt;IC 优先级链与 MaxIterations 计数。
/// </summary>
public static class TerminationDecider
{
    /// <summary>判定是否终止并给出决策。短路口：halt=1 → TM 优先于 token 耗尽。</summary>
    public static bool ShouldTerminate(ILoopState ctx, out LoopDecision decision)
    {
        if (ctx.Flag.Halt)
        {
            decision = LoopDecision.TripTerminated;
            return true;
        }

        if (ctx.Token.IsExhausted)
        {
            decision = LoopDecision.Timeout;
            return true;
        }

        decision = LoopDecision.Continue;
        return false;
    }
}
