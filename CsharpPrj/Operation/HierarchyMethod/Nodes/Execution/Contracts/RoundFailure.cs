using System;
using System.Collections.Generic;
using Modules.KeySpace.Diagnostics;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 运行期失败码表（单源 = <see cref="KeyReasonCodes"/>）：本类只做类型化别名，禁止新写字面量。
/// 与生成期码共享命名空间前缀，但只有 <c>exec.*</c> / <c>seed.*</c> / <c>round.*</c> 进轮内失败。
/// </summary>
public static class RoundFailureCodes
{
    public const string ExecPortMissing = KeyReasonCodes.ExecPortMissing;
    public const string ExecStepMissing = KeyReasonCodes.ExecStepMissing;
    public const string ExecUndeclaredInport = KeyReasonCodes.ExecUndeclaredInport;
    public const string ExecUndeclaredOutport = KeyReasonCodes.ExecUndeclaredOutport;
    public const string ExecBindFailed = KeyReasonCodes.ExecBindFailed;
    public const string ExecFunctionMissing = KeyReasonCodes.ExecFunctionMissing;
    public const string SeedCarrierMissing = KeyReasonCodes.SeedCarrierMissing;
    public const string SeedPortUncovered = KeyReasonCodes.SeedPortUncovered;
    public const string RoundCriterionUnmet = KeyReasonCodes.RoundCriterionUnmet;
    public const string ExecExecutionFailure = KeyReasonCodes.ExecExecutionFailure;

    /// <summary>提示词前置失败（B2）：提示词端口缺失（先于 T2 端口契约归因）。</summary>
    public const string PromptMissing = KeyReasonCodes.PromptMissing;

    /// <summary>运行身份缺失（R0 一等失败码）：不再以空作用域继续执行（ERR-01）。</summary>
    public const string IdentityMissing = KeyReasonCodes.IdentityMissing;

    /// <summary>守卫空输入（空输入 = 轮次不成立，不得继续走 provide_prompt/cline_execute）。</summary>
    public const string GuardEmptyInput = KeyReasonCodes.GuardEmptyInput;
}

/// <summary>
/// 失败一等表示（单源）：<see cref="CompletedSteps"/> 必须由调用方注入（禁止默认空集），
/// <see cref="ProducedPorts"/> 只表示实际产出键，声明外键另立 <see cref="UndeclaredKeys"/>。
/// </summary>
public sealed record RoundFailure(
    string Code,
    string FailedStep,
    IReadOnlyList<string> CompletedSteps,
    IReadOnlyList<string> ProducedPorts,
    string Message,
    IReadOnlyList<string>? UndeclaredKeys = null,
    IReadOnlyDictionary<string, string>? Facts = null)
{
    public static IReadOnlyList<string> None { get; } = Array.Empty<string>();

    public static RoundFailure PortMissing(
        string step, IReadOnlyList<string> completed, IReadOnlyList<string> produced, string detail)
        => new(RoundFailureCodes.ExecPortMissing, step, completed, produced, detail);

    public static RoundFailure StepMissing(string step, IReadOnlyList<string> completed, string detail = "")
        => new(RoundFailureCodes.ExecStepMissing, step, completed, None,
            string.IsNullOrWhiteSpace(detail) ? $"声明步骤未执行却判定成功：{step}" : detail);

    public static RoundFailure BindFailed(
        string step, IReadOnlyList<string> completed, IReadOnlyList<string> produced, string detail)
        => new(RoundFailureCodes.ExecBindFailed, step, completed, produced, detail);

    public static RoundFailure FunctionMissing(
        string step, IReadOnlyList<string> completed, string detail)
        => new(RoundFailureCodes.ExecFunctionMissing, step, completed, None, detail);

    public static RoundFailure SeedPortUncovered(string step, IReadOnlyList<string> missing)
        => new(RoundFailureCodes.SeedPortUncovered, step, None, None,
            $"首步入参无种子来源：[{string.Join(",", missing)}]",
            UndeclaredKeys: missing);

    public static RoundFailure CriterionUnmet(string step, IReadOnlyList<string> completed, string detail)
        => new(RoundFailureCodes.RoundCriterionUnmet, step, completed, None, detail);

    public static RoundFailure ExecutionFailed(string step, IReadOnlyList<string> completed, string detail)
        => new(RoundFailureCodes.ExecExecutionFailure, step, completed, None, detail);

    /// <summary>提示词前置失败（一等步骤失败；先于端口契约归因，避免被 exec.port-missing 覆盖）。</summary>
    public static RoundFailure PromptMissing(string step, IReadOnlyList<string> missing, string detail)
        => new(RoundFailureCodes.PromptMissing, step, None, None, detail, UndeclaredKeys: missing);

    /// <summary>
    /// 运行身份缺失（结构缺口）：作用域不可锚定 ⇒ 显式失败，禁空作用域继续（ERR-01 / SR-04）。
    /// </summary>
    public static RoundFailure IdentityMissing(string step, string detail = "")
        => new(RoundFailureCodes.IdentityMissing, step, None, None,
            string.IsNullOrWhiteSpace(detail)
                ? "运行身份缺失（RunRef 为空）：作用域不可锚定，禁止以空作用域/常量顶替继续执行"
                : detail);

    /// <summary>D06：无轮载体 ⇒ 种子段整段缺失（fail-fast，禁静默空转）。</summary>
    public static RoundFailure SeedCarrierMissing(string step)
        => new(RoundFailureCodes.SeedCarrierMissing, step, None, None,
            "轮载体缺失：seed/harvest 无落点，禁止以空种子继续执行");

    /// <summary>D01：guard_input 空输入 ⇒ 强类型失败（不再由缺端口 exec.port-missing 代为归因）。</summary>
    public static RoundFailure GuardEmptyInput(string step)
        => new(KeyReasonCodes.GuardEmptyInput, step, None, None,
            "guard_input 收到空输入：轮次不成立（需先在分发面补齐 input）");

    public RoundFailureException ToException() => new(this);
}

/// <summary>失败的异常载体（执行边界唯一抛出形态；上层据 <see cref="Failure"/> 渲染/落库）。</summary>
public sealed class RoundFailureException : InvalidOperationException
{
    public RoundFailureException(RoundFailure failure)
        : base($"[{failure.Code}] step={failure.FailedStep}, completed=[{string.Join(",", failure.CompletedSteps)}], " +
               $"produced=[{string.Join(",", failure.ProducedPorts)}]: {failure.Message}")
        => Failure = failure;

    public RoundFailure Failure { get; }
}
