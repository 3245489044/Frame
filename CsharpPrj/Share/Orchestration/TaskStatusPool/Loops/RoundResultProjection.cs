using System;
using System.Collections.Generic;

namespace Share.TaskStatusPool.Loops;

/// <summary>
/// 轮结果 → 引擎动作结果投影的唯一不变量：<c>Ok</c> 只由“已产出”派生；空产物强制 EmptyResult。
/// </summary>
public static class RoundResultProjection
{
    public static AxiomActionResult ToActionResult(
        bool produced,
        bool empty,
        string? error,
        string? text,
        ErrorClass? errorClass,
        Dictionary<string, object> outputs)
    {
        ArgumentNullException.ThrowIfNull(outputs);

        return new AxiomActionResult
        {
            Ok = produced,
            Error = error,
            Text = text ?? string.Empty,
            ErrorClass = empty ? ErrorClass.EmptyResult : errorClass,
            Outputs = outputs,
        };
    }

    /// <summary>
    /// 二值投影：<paramref name="empty"/> 或未产出 ⇒ <see cref="StepOutcome.Fail"/>（EMPTY_OUTPUT）。
    /// Partial 产物挂 Facts，不制造第三态。
    /// </summary>
    public static StepOutcome ToStepOutcome(
        bool produced,
        bool empty,
        string? error,
        string? text,
        ErrorClass? errorClass,
        IReadOnlyDictionary<string, object?> outputs)
    {
        ArgumentNullException.ThrowIfNull(outputs);

        if (!produced)
        {
            var cls = errorClass ?? ErrorClass.ExecutionFailure;
            return new StepOutcome.Fail(RoundLifecycle.ToFailure(cls, error));
        }

        if (empty || string.IsNullOrWhiteSpace(text))
            return new StepOutcome.Fail(RoundLifecycle.ToFailure(ErrorClass.EmptyResult, error));

        return new StepOutcome.Ok(outputs);
    }
}
