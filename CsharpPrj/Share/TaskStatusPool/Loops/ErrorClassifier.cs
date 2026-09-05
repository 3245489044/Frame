using System;
using System.Collections.Generic;

namespace Share.TaskStatusPool.Loops;

/// <summary>作用域 = 合法动作子集（clinerules Type.Scope / formula §2.2）。</summary>
public enum Scope
{
    /// <summary>循环体：{halt, retry, switch}</summary>
    LoopBody,

    /// <summary>单次调用：{halt, retry}</summary>
    SingleCall,

    /// <summary>过程：{halt}</summary>
    Procedure,

    /// <summary>全局：{halt}</summary>
    Global,
}

/// <summary>动作（clinerules Type.Action；continue 为循环级继续决策，不受 Scope 约束）。</summary>
public enum Action
{
    Continue,
    Retry,
    Switch,
    Halt,
}

/// <summary>
/// 执行边界预算耗尽标记（clinerules TerminationPriority：预算终止 = 引擎内可判定，区别于协作式取消）。
/// 由更高层（Operation.HierarchyMethod.Center 边界异常族 / 消费方单步超时异常）实现；分类器据此归为 Timeout，
/// 避免「有界窗口到点」被默认误归为 ExecutionFailure。属协议标记：Share 层不引用任何 Operation/Agent 类型。
/// </summary>
public interface IBudgetExhausted
{
}

/// <summary>
/// 错误分类器（clinerules axioms §4 / formula §5.2 SM.ErrorHandler）。分类（纯函数）与动作域（作用域策略参数注入）分离：
/// 分类逻辑唯一（ClassifyError），动作由 Scope 与 FailureBudget.level 决定。
/// 通用任务循环 canonical（SingleImplementation / TH-12），消费方（Agent 等使用层）仅调用本类，不重复实现。
/// </summary>
public static class ErrorClassifier
{
    /// <summary>纯映射，无动作。元素权威基：AX.FailureSubtyping。</summary>
    public static ErrorClass ClassifyError(Exception ex)
    {
        return ex switch
        {
            IBudgetExhausted => ErrorClass.Timeout, // 有界窗口到点（步/轮/存储预算耗尽）→ 超时，非执行失败
            TimeoutException => ErrorClass.Timeout,
            OperationCanceledException => ErrorClass.Timeout,
            FormatException => ErrorClass.ParseError,
            OutOfMemoryException => ErrorClass.ResourceExhausted,
            InvalidOperationException => ErrorClass.ExecutionFailure,
            _ => ErrorClass.ExecutionFailure,
        };
    }

    /// <summary>按错误文本关键词分类（兼容工作流字符串错误）。</summary>
    public static ErrorClass ClassifyError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return ErrorClass.ExecutionFailure;
        var e = error.ToLowerInvariant();
        if (e.Contains("timeout") || e.Contains("超时")) return ErrorClass.Timeout;
        if (e.Contains("parse") || e.Contains("格式") || e.Contains("syntax")) return ErrorClass.ParseError;
        if (e.Contains("empty") || e.Contains("空结果") || e.Contains("null")) return ErrorClass.EmptyResult;
        if (e.Contains("编译") || e.Contains("compile")) return ErrorClass.CompilationError;
        if (e.Contains("不可恢复") || e.Contains("irrecoverable")) return ErrorClass.Irrecoverable;
        if (e.Contains("资源") || e.Contains("resource")) return ErrorClass.ResourceExhausted;
        return ErrorClass.ExecutionFailure;
    }

    /// <summary>各 Scope 的合法动作子集（clinerules formula SM.ErrorHandler 注入表）。</summary>
    public static IReadOnlySet<Action> AllowedActions(Scope scope) => scope switch
    {
        Scope.LoopBody => new HashSet<Action> { Action.Halt, Action.Retry, Action.Switch },
        Scope.SingleCall => new HashSet<Action> { Action.Halt, Action.Retry },
        Scope.Procedure => new HashSet<Action> { Action.Halt },
        Scope.Global => new HashSet<Action> { Action.Halt },
        _ => new HashSet<Action> { Action.Halt },
    };

    /// <summary>
    /// 统一动作裁决 handle(error, Scope)：按 Scope 合法子集与 FailureBudget.level 择一动作。
    /// level=0 → retry 正常；level=1 → retry 受限；level=2 → switch/暂停；level=3 → halt。
    /// continue 为循环级决策，不由本方法产出（由引擎 Decide 阶段裁决）。
    /// </summary>
    public static Action Handle(ErrorClass error, Scope scope, int budgetLevel)
    {
        var allowed = AllowedActions(scope);

        if (error == ErrorClass.Irrecoverable || budgetLevel >= FailureBudget.MaxLevel)
            return Action.Halt;

        if (budgetLevel == 2)
            return allowed.Contains(Action.Switch) ? Action.Switch : Action.Halt;

        // level 0 / 1 → 允许重试则重试（level=1 由上层收紧 RetryLimit，本裁决不重复计数）
        return allowed.Contains(Action.Retry) ? Action.Retry : Action.Halt;
    }
}
