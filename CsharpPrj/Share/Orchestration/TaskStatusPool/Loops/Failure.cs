using System;
using System.Collections.Generic;

namespace Share.TaskStatusPool.Loops;

/// <summary>
/// 失败分类（二值终止语义）：Contract = 编程/契约错误（必红），Domain = 业务域失败，
/// Environment = 环境/进程级失败。诊断维度只进日志与 Facts，不参与控制流分支。
/// </summary>
public enum FailureClass
{
    Contract,
    Domain,
    Environment,
}

/// <summary>
/// 失败事实（二值终止的唯一失败载体）。<see cref="Code"/> 须取自 <see cref="FailureCodes"/> 注册表，
/// 禁自由字符串；<see cref="Facts"/> 只承载事实（如 Partial 产物），不得承载"是否继续"的裁决意图。
/// </summary>
public readonly record struct Failure(
    FailureClass Class,
    string Code,
    string Message,
    string? Step = null,
    IReadOnlyDictionary<string, object?>? Facts = null)
{
    public static Failure Contract(string code, string message, string? step = null,
        IReadOnlyDictionary<string, object?>? facts = null)
        => new(FailureClass.Contract, code, message, step, facts);

    public static Failure Domain(string code, string message, string? step = null,
        IReadOnlyDictionary<string, object?>? facts = null)
        => new(FailureClass.Domain, code, message, step, facts);

    public static Failure Environment(string code, string message, string? step = null,
        IReadOnlyDictionary<string, object?>? facts = null)
        => new(FailureClass.Environment, code, message, step, facts);

    public override string ToString() => $"[{Class}] {Code}: {Message}";
}

/// <summary>
/// 执行单元的唯一结果类型（AX-ZR BinaryOutcome）：<c>Ok</c> / <c>Fail</c> 二值，无第三态。
/// Partial（部分产出）归 <c>Fail</c>，产物挂 <see cref="Failure.Facts"/>。
/// </summary>
public abstract record StepOutcome
{
    private StepOutcome() { }

    /// <summary>成功：<see cref="Outputs"/> 恒非空（空产物由调用方转 Fail(EMPTY_OUTPUT)）。</summary>
    public sealed record Ok(IReadOnlyDictionary<string, object?> Outputs) : StepOutcome;

    /// <summary>失败：失败事实唯一载体。</summary>
    public sealed record Fail(Failure Failure) : StepOutcome;

    public bool IsOk => this is Ok;
}

/// <summary>
/// 失败码注册表（R10 判据载体：Failure.Code 必须在此登记，禁散文码）。
/// </summary>
public static class FailureCodes
{
    // 契约/编程错误
    public const string ContractUnassigned = "CONTRACT_UNASSIGNED";
    public const string ContractExecutorMissing = "CONTRACT_EXECUTOR_MISSING";
    public const string TailUnresolved = "TAIL_UNRESOLVED";
    /// <summary>骨架提前终止（失败的一等表示）：执行步数 &lt; 声明步数，禁再以索引越界/计数错位表达。</summary>
    public const string EarlyFault = "EARLY_FAULT";
    public const string UnjustifiedRetry = "UNJUSTIFIED_RETRY";
    public const string InvalidRetry = "INVALID_RETRY";
    public const string AttemptDoubleWrite = "ATTEMPT_DOUBLE_WRITE";
    public const string PostFailSideEffect = "POST_FAIL_SIDE_EFFECT";
    public const string Unhandled = "UNHANDLED";
    public const string JudgeMissing = "JUDGE_MISSING";
    public const string Unresolved = "UNRESOLVED";
    public const string Cancelled = "CANCELLED";

    // 业务/执行失败
    public const string EmptyOutput = "EMPTY_OUTPUT";
    public const string Timeout = "TIMEOUT";
    public const string ExecutionFailure = "EXECUTION_FAILURE";
    public const string ParseError = "PARSE_ERROR";
    public const string CompilationError = "COMPILATION_ERROR";
    public const string PartialOutput = "PARTIAL_OUTPUT";
    public const string NoProgress = "NO_PROGRESS";

    // 环境失败
    public const string EnvNoWorker = "ENV_NO_WORKER";
    public const string EnvDaemonDown = "ENV_DAEMON_DOWN";
}

/// <summary>失败即异常出口（禁止 catch 后继续；捕获方只能 <c>throw;</c> 或转 <see cref="StepOutcome.Fail"/>）。</summary>
public sealed class FailureException : Exception
{
    public FailureException(Failure failure)
        : base($"[{failure.Class}] {failure.Code}: {failure.Message}")
        => Failure = failure;

    public Failure Failure { get; }
}
