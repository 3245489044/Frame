using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.TaskStatusPool.Loops;
using Action = Share.TaskStatusPool.Loops.Action;

namespace Share.StateManagement.Tests;

/// <summary>
/// 错误处理原语单测（canonical 于 Share.TaskStatusPool.Loops.ErrorClassifier，SingleImplementation / TH-12）。
/// 覆盖：ClassifyError 纯映射（异常 / 文本）、AllowedActions 作用域注入表、Handle 表驱动动作裁决。
/// </summary>
[TestClass]
public class ErrorClassifierTests
{
    // ---- ClassifyError(Exception)：纯映射，无动作 ----

    [TestMethod]
    public void ClassifyError_Exception_TimeoutMapping()
    {
        Assert.AreEqual(ErrorClass.Timeout, ErrorClassifier.ClassifyError(new TimeoutException()));
        Assert.AreEqual(ErrorClass.Timeout, ErrorClassifier.ClassifyError(new OperationCanceledException()));
    }

    [TestMethod]
    public void ClassifyError_Exception_FormatAndResource()
    {
        Assert.AreEqual(ErrorClass.ParseError, ErrorClassifier.ClassifyError(new FormatException()));
        Assert.AreEqual(ErrorClass.ResourceExhausted, ErrorClassifier.ClassifyError(new OutOfMemoryException()));
    }

    [TestMethod]
    public void ClassifyError_Exception_DefaultExecutionFailure()
    {
        Assert.AreEqual(ErrorClass.ExecutionFailure, ErrorClassifier.ClassifyError(new InvalidOperationException()));
        Assert.AreEqual(ErrorClass.ExecutionFailure, ErrorClassifier.ClassifyError(new Exception()));
    }

    // ---- ClassifyError(string)：文本关键词分类 ----

    [TestMethod]
    public void ClassifyError_Text_EmptyIsExecutionFailure()
    {
        Assert.AreEqual(ErrorClass.ExecutionFailure, ErrorClassifier.ClassifyError((string?)null));
        Assert.AreEqual(ErrorClass.ExecutionFailure, ErrorClassifier.ClassifyError(string.Empty));
        Assert.AreEqual(ErrorClass.ExecutionFailure, ErrorClassifier.ClassifyError("   "));
    }

    [TestMethod]
    [DataRow("request timeout", ErrorClass.Timeout)]
    [DataRow("请求超时", ErrorClass.Timeout)]
    [DataRow("parse error at line 3", ErrorClass.ParseError)]
    [DataRow("格式不合法", ErrorClass.ParseError)]
    [DataRow("syntax error", ErrorClass.ParseError)]
    [DataRow("empty result", ErrorClass.EmptyResult)]
    [DataRow("空结果", ErrorClass.EmptyResult)]
    [DataRow("编译失败", ErrorClass.CompilationError)]
    [DataRow("compile failed", ErrorClass.CompilationError)]
    [DataRow("irrecoverable failure", ErrorClass.Irrecoverable)]
    [DataRow("不可恢复", ErrorClass.Irrecoverable)]
    [DataRow("资源耗尽", ErrorClass.ResourceExhausted)]
    [DataRow("resource exhausted", ErrorClass.ResourceExhausted)]
    [DataRow("some unknown error", ErrorClass.ExecutionFailure)]
    public void ClassifyError_Text_KeywordMapping(string error, ErrorClass expected)
        => Assert.AreEqual(expected, ErrorClassifier.ClassifyError(error));

    // ---- AllowedActions：作用域注入表（formula SM.ErrorHandler） ----

    [TestMethod]
    public void AllowedActions_LoopBody_AllowsHaltRetrySwitch()
    {
        var allowed = ErrorClassifier.AllowedActions(Scope.LoopBody);
        Assert.IsTrue(allowed.Contains(Action.Halt));
        Assert.IsTrue(allowed.Contains(Action.Retry));
        Assert.IsTrue(allowed.Contains(Action.Switch));
        Assert.IsFalse(allowed.Contains(Action.Continue)); // continue 为循环级决策，不受 Scope 约束
    }

    [TestMethod]
    public void AllowedActions_SingleCall_NoSwitch()
    {
        var allowed = ErrorClassifier.AllowedActions(Scope.SingleCall);
        Assert.IsTrue(allowed.Contains(Action.Halt));
        Assert.IsTrue(allowed.Contains(Action.Retry));
        Assert.IsFalse(allowed.Contains(Action.Switch));
    }

    [TestMethod]
    public void AllowedActions_ProcedureAndGlobal_OnlyHalt()
    {
        foreach (var scope in new[] { Scope.Procedure, Scope.Global })
        {
            var allowed = ErrorClassifier.AllowedActions(scope);
            Assert.IsTrue(allowed.Contains(Action.Halt));
            Assert.IsFalse(allowed.Contains(Action.Retry));
            Assert.IsFalse(allowed.Contains(Action.Switch));
        }
    }

    // ---- Handle(error, Scope, budgetLevel)：动作裁决（FailureBudgetEscalation 语义） ----

    [TestMethod]
    public void Handle_Irrecoverable_AlwaysHalt()
        => Assert.AreEqual(Action.Halt, ErrorClassifier.Handle(ErrorClass.Irrecoverable, Scope.LoopBody, 0));

    [TestMethod]
    public void Handle_BudgetTripped_AlwaysHalt()
        => Assert.AreEqual(Action.Halt, ErrorClassifier.Handle(ErrorClass.ExecutionFailure, Scope.LoopBody, FailureBudget.MaxLevel));

    [TestMethod]
    public void Handle_LevelTwo_SwitchWhenAllowedElseHalt()
    {
        Assert.AreEqual(Action.Switch, ErrorClassifier.Handle(ErrorClass.ExecutionFailure, Scope.LoopBody, 2));
        Assert.AreEqual(Action.Halt, ErrorClassifier.Handle(ErrorClass.ExecutionFailure, Scope.SingleCall, 2));
        Assert.AreEqual(Action.Halt, ErrorClassifier.Handle(ErrorClass.ExecutionFailure, Scope.Procedure, 2));
    }

    [TestMethod]
    public void Handle_LevelZeroOne_RetryWhenAllowedElseHalt()
    {
        Assert.AreEqual(Action.Retry, ErrorClassifier.Handle(ErrorClass.ExecutionFailure, Scope.LoopBody, 0));
        Assert.AreEqual(Action.Retry, ErrorClassifier.Handle(ErrorClass.ExecutionFailure, Scope.SingleCall, 1));
        Assert.AreEqual(Action.Halt, ErrorClassifier.Handle(ErrorClass.ExecutionFailure, Scope.Procedure, 0));
        Assert.AreEqual(Action.Halt, ErrorClassifier.Handle(ErrorClass.ExecutionFailure, Scope.Global, 1));
    }

    [TestMethod]
    public void Handle_PartialOutput_DoesNotTripAtHighBudget_UnlessAtTripLevel()
    {
        // partial_output 不升级（"部分可用"严重度低于格式违规）：低级别下可重试。
        Assert.AreEqual(Action.Retry, ErrorClassifier.Handle(ErrorClass.PartialOutput, Scope.LoopBody, 0));
        // 熔断打满（level=3）时无论何种错误类别一律 halt。
        Assert.AreEqual(Action.Halt, ErrorClassifier.Handle(ErrorClass.PartialOutput, Scope.LoopBody, FailureBudget.MaxLevel));
    }
}
