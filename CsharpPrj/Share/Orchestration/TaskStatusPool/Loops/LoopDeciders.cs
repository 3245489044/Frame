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
            // 遥测：不可恢复失败打满记账，终止由 StepOutcome.Fail 直接决定（不再置 ControlFlag）。
            ctx.Budget.Trip();
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
/// 终止判定（二值）：预算/空转计数器已降为遥测，不再产生终止。
/// <see cref="LoopDecision.Complete"/> / <see cref="LoopDecision.Abort"/> 由 <see cref="RoundLifecycle.Decide"/> 直接产出。
/// 保留本类仅为兼容旧调用点的"是否终态"查询，恒返回 false（不终止）。
/// </summary>
public static class TerminationDecider
{
    /// <summary>恒 false：终止只能由 Fail 直接决定（AX-ZR C2 失败即终止，无预算长跑）。</summary>
    [System.Obsolete("二值终止：终止由 RoundLifecycle.Decide/StepOutcome.Fail 直接决定；本判定恒 false，S7 删除。")]
    public static bool ShouldTerminate(ILoopState ctx, out LoopDecision decision)
    {
        decision = LoopDecision.Abort;
        return false;
    }
}
