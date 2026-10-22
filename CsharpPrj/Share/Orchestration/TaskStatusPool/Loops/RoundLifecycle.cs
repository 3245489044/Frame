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
    public bool Terminal { get; init; }
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

        // 进度 + 熔断记账（遥测：只进日志/指标，不参与终止裁决）。
        var eval = evaluate != null ? evaluate(result, errCls) : InferEval(result, errCls);
        ProgressUpdater.Apply(ctx, eval);
        BudgetEscalator.Apply(ctx, errCls, eval);

        // 二值终止：终止只由 StepOutcome.Fail 直接决定（Decide），此处不再置 halt。
        return eval;
    }

    /// <summary>
    /// 二值执行单元（AX-ZR NoInternalRetry）：恰一次执行，禁 for/while/递归/失败重跑。
    /// 异常一律转 <see cref="StepOutcome.Fail"/>，向后传播的只有取消。
    /// </summary>
    public static async Task<StepOutcome> ExecuteOnceAsync(
        ILoopState ctx,
        Func<ILoopState, CancellationToken, Task<AxiomActionResult>> execute,
        Func<AxiomActionResult, ErrorClass?, EvalOutcome>? evaluate,
        CancellationToken ct = default)
    {
        PortGuard.Required(ctx, nameof(ctx));
        PortGuard.Required(execute, nameof(execute));

        AxiomActionResult result;
        try
        {
            result = await execute(ctx, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new StepOutcome.Fail(ToFailure(ErrorClassifier.ClassifyError(ex), ex.Message));
        }

        var errCls = Collect(ctx, result);
        var eval = Evaluate(ctx, result, errCls, evaluate);
        return Project(result, eval, errCls);
    }

    /// <summary>
    /// 二值投影（唯一判定点）：成功 = Ok ∧ pass ∧ 产物非空；其余一律 Fail（Partial→Fail，产物入 Facts）。
    /// </summary>
    public static StepOutcome Project(AxiomActionResult result, EvalOutcome eval, ErrorClass? errCls)
    {
        PortGuard.Required(result, nameof(result));

        if (result.Ok && eval == EvalOutcome.Pass && !string.IsNullOrWhiteSpace(result.Text))
        {
            var outputs = new Dictionary<string, object?>();
            foreach (var kv in result.Outputs) outputs[kv.Key] = kv.Value;
            return new StepOutcome.Ok(outputs);
        }

        if (result.Ok && string.IsNullOrWhiteSpace(result.Text))
            return new StepOutcome.Fail(Failure.Domain(FailureCodes.EmptyOutput, "产物为空（EMPTY_OUTPUT）。"));

        return new StepOutcome.Fail(ToFailure(errCls ?? ErrorClass.ExecutionFailure, result.Error));
    }

    /// <summary>错误分类 → 失败事实（诊断标签只进 Code/Class，不进控制流分支）。</summary>
    public static Failure ToFailure(ErrorClass cls, string? message)
        => cls switch
        {
            ErrorClass.ParseError => Failure.Contract(FailureCodes.ParseError, message ?? "解析错误。"),
            ErrorClass.CompilationError => Failure.Contract(FailureCodes.CompilationError, message ?? "编译错误。"),
            ErrorClass.EmptyResult => Failure.Domain(FailureCodes.EmptyOutput, message ?? "空结果。"),
            ErrorClass.PartialOutput => Failure.Domain(FailureCodes.PartialOutput, message ?? "部分产出。"),
            ErrorClass.Timeout => Failure.Environment(FailureCodes.Timeout, message ?? "超时。"),
            ErrorClass.ResourceExhausted => Failure.Environment(FailureCodes.ExecutionFailure, message ?? "资源耗尽。"),
            _ => Failure.Domain(FailureCodes.ExecutionFailure, message ?? "执行失败。"),
        };

    /// <summary>④ Decide —— 二值终止：Ok → Complete；Fail → Abort。预算/空转计数器不再参与终止。</summary>
    public static LoopDecision Decide(StepOutcome outcome)
    {
        PortGuard.Required(outcome, nameof(outcome));
        return outcome is StepOutcome.Ok ? LoopDecision.Complete : LoopDecision.Abort;
    }

    /// <summary>
    /// 二值单轮（①→④，无 Adjust）：轮号推进由 <see cref="AttemptLedger"/> 单点负责，本方法不写轮号。
    /// </summary>
    public static async Task<AxiomRound> RunAsync(
        ILoopState ctx,
        Func<ILoopState, CancellationToken, Task<AxiomActionResult>> execute,
        Func<AxiomActionResult, ErrorClass?, EvalOutcome>? evaluate,
        CancellationToken ct = default)
    {
        var outcome = await ExecuteOnceAsync(ctx, execute, evaluate, ct).ConfigureAwait(false);
        var decision = Decide(outcome);

        AxiomActionResult result;
        if (outcome is StepOutcome.Ok ok)
        {
            var outputs = new Dictionary<string, object>();
            foreach (var kv in ok.Outputs)
                if (kv.Value is not null) outputs[kv.Key] = kv.Value;

            result = new AxiomActionResult
            {
                Ok = true,
                Text = ctx.LastResultText ?? string.Empty,
                Outputs = outputs,
            };
        }
        else
        {
            var failure = ((StepOutcome.Fail)outcome).Failure;
            result = new AxiomActionResult
            {
                Ok = false,
                Error = failure.Message,
                ErrorClass = ctx.LastEval == EvalOutcome.ParseError ? ErrorClass.ParseError : ErrorClass.ExecutionFailure,
            };
        }

        return new AxiomRound
        {
            Round = ctx.Round,
            Result = result,
            Eval = ctx.LastEval,
            Decision = decision,
            NextHint = null,
        };
    }

    private static EvalOutcome InferEval(AxiomActionResult result, ErrorClass? errCls)
    {
        if (!result.Ok)
            return errCls == ErrorClass.ParseError ? EvalOutcome.ParseError : EvalOutcome.Fail;
        return string.IsNullOrWhiteSpace(result.Text) ? EvalOutcome.Fail : EvalOutcome.Pass;
    }
}
