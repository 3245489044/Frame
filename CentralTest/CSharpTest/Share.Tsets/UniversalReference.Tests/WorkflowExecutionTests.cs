
using CentralConsole.Testing;
using CentralConsole.Testing.Attributes;
using CentralConsole.Testing.Builders;
using CentralConsole.Testing.Core;
using CentralConsole.Testing.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UniversalWorkflowFramework.ActionMapping.Models;
using UniversalWorkflowFramework.Core.Interfaces;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.WorkflowExecution.Executors;

namespace BasicMethod.Tests.UniversalReference;

[TestClass]
public class WorkflowExecutionTests : UniversalReferenceTestBase
{
    [TestMethod]
    public async Task WorkflowExecution_AllTests()
    {
        var result = await new TestGroupBuilder("WorkflowExecution")
            .Add("Execute_UIAction_ShouldReturnResult", async () =>
            {
                var executor = WorkflowExecutorFactory.Create();
                await executor.InitializeAsync();

                var uiAction = new UIAction
                {
                    ActionType = "submit",
                    Parameters = new Dictionary<string, object>
                    {
                        { "formId", "login-form" },
                        { "username", "testuser" }
                    }
                };

                var actionResult = await executor.ExecuteAsync(uiAction, isUIAction: true);
                return actionResult != null;
            })
            .Add("Execute_UIAction_Click_ShouldReturnResult", async () =>
            {
                var executor = WorkflowExecutorFactory.Create();
                await executor.InitializeAsync();

                var uiAction = new UIAction
                {
                    ActionType = "click",
                    Parameters = new Dictionary<string, object>
                    {
                        { "buttonId", "search-btn" },
                        { "query", "test" }
                    }
                };

                var actionResult = await executor.ExecuteAsync(uiAction, isUIAction: true);
                return actionResult != null;
            })
            .Add("Execute_AIAction_ShouldReturnResult", async () =>
            {
                var executor = WorkflowExecutorFactory.Create();
                await executor.InitializeAsync();

                var aiAction = new AIAction
                {
                    ActionType = "ai_analyze",
                    Parameters = new Dictionary<string, object>
                    {
                        { "input", "analyze this" },
                        { "model", "gpt-4" }
                    }
                };

                var aiResult = await executor.ExecuteAsync(aiAction, isUIAction: false);
                return aiResult != null;
            })
            .Add("ExecuteBranch_ShouldReturnResult", async () =>
            {
                var executor = WorkflowExecutorFactory.Create();
                await executor.InitializeAsync();

                var branchResult = await executor.ExecuteBranchAsync("checkout-flow", new Dictionary<string, object>
                {
                    { "userId", "u123" },
                    { "amount", 99.99 }
                });
                return branchResult != null;
            })
            .Add("ExecuteBranch_PaymentFlow_ShouldReturnResult", async () =>
            {
                var executor = WorkflowExecutorFactory.Create();
                await executor.InitializeAsync();

                var branchResult = await executor.ExecuteBranchAsync("payment-flow", new Dictionary<string, object>
                {
                    { "orderId", "order-001" },
                    { "method", "credit_card" }
                });
                return branchResult != null;
            })
            .Add("ValidateExecution_Success_ShouldBeValid", async () =>
            {
                var executor = WorkflowExecutorFactory.Create();
                await executor.InitializeAsync();

                var actionResult = await executor.ExecuteAsync(
                    new UIAction { ActionType = "submit", Parameters = new() }, isUIAction: true);

                var validation = await executor.ValidateExecutionAsync("test-workflow", actionResult);
                return validation != null;
            })
            .Add("ValidateExecution_Failure_ShouldBeInvalid", async () =>
            {
                var executor = WorkflowExecutorFactory.Create();
                await executor.InitializeAsync();

                var successResult = await executor.ExecuteAsync(
                    new UIAction { ActionType = "submit", Parameters = new() }, isUIAction: true);
                var failedResult = new ExecutionResult { Status = ExecutionStatus.Failed, Error = "测试错误" };

                var validation = await executor.ValidateExecutionAsync("failed-workflow", successResult, failedResult);
                return validation != null;
            })
            .RunAsync();

        result.AssertAllPassed(printSummary: true);
    }
}
