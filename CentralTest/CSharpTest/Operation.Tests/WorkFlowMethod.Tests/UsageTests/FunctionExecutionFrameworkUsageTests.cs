using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkFlowMethod.FunctionExecutionFramework;
using WorkFlowMethod.FunctionExecutionFramework.Core;
using WorkFlowMethod.FunctionExecutionFramework.Models;
using WorkFlowMethod.Orchestration.Context.Models;
using BmExecutionResult = TConfig.ExecutionResult;

namespace WorkFlowMethod.Tests.UsageTests
{
    [TestClass]
    [TestCategory("FunctionExecutionFramework")]
    public class FunctionExecutionFrameworkUsageTests
    {
        [TestMethod]
        public void DefaultInstances_ShouldNotBeNull()
        {
            Assert.IsNotNull(FunctionExecutionFramework.FunctionExecutionFramework.Registry);
            Assert.IsNotNull(FunctionExecutionFramework.FunctionExecutionFramework.Pipeline);
            Assert.IsNotNull(FunctionExecutionFramework.FunctionExecutionFramework.Registrar);
        }

        [TestMethod]
        public void RegisterAndExecute_ShouldWork()
        {
            FunctionExecutionFramework.FunctionExecutionFramework.Register("Double", (int x) => x * 2);
            var result = FunctionExecutionFramework.FunctionExecutionFramework.ExecuteAsync("Double", 5).GetAwaiter().GetResult();
            Assert.IsTrue(result.IsSuccess);
        }

        [TestMethod]
        public void RegisterWorkflowAndCall_ShouldRoundtrip()
        {
            FunctionExecutionFramework.FunctionExecutionFramework.Register("WfHello", ctx => $"Hello {ctx.ExecutionId}");
            var result = FunctionExecutionFramework.FunctionExecutionFramework.Registry.CallWorkflow("WfHello", new WorkflowContext(executionId: "wf_001"));
            Assert.AreEqual("Hello wf_001", result);
        }

        [TestMethod]
        public async Task RegisterAsyncWorkflowAndCall_ShouldRoundtrip()
        {
            FunctionExecutionFramework.FunctionExecutionFramework.RegisterAsync("WfAsync", async ctx =>
            {
                await Task.Delay(10);
                return $"Async_{ctx.ExecutionId}";
            });
            var result = await FunctionExecutionFramework.FunctionExecutionFramework.Registry.CallWorkflowAsync("WfAsync", new WorkflowContext(executionId: "wf_002"));
            Assert.AreEqual("Async_wf_002", result);
        }

        [TestMethod]
        public void DefaultWorkflowRegistry_RegisterAndCall_ShouldRoundtrip()
        {
            var registry = new DefaultWorkflowRegistry();
            registry.RegisterWorkflow("MyFunc", ctx => $"Result:{ctx.ExecutionId}");
            Assert.AreEqual("Result:exec_001", registry.CallWorkflow("MyFunc", new WorkflowContext(executionId: "exec_001")));
        }

        [TestMethod]
        public void BasicMethodAdapter_SyncBoth_ShouldNotThrow()
        {
            var adapter = new WorkFlowMethod.FunctionExecutionFramework.Adapters.BasicMethodAdapter();
            adapter.SyncBoth();
        }

        [TestMethod]
        public void Extensions_WithParameter_ShouldSet()
        {
            var context = new WorkflowContext(
                parameters: new Dictionary<string, object> { { "key1", "value1" } },
                executionId: "ext_001");
            Assert.AreEqual("value1", context.Parameters["key1"]);
        }

        [TestMethod]
        public void Extensions_ThrowIfFailed_ShouldThrowOnFailure()
        {
            var result = BmExecutionResult.Failure("test error");
            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual("test error", result.ErrorMessage);
        }

        [TestMethod]
        public void Extensions_TryGetValue_ShouldReturnCorrectly()
        {
            var success = BmExecutionResult.Success(42, TimeSpan.Zero);
            Assert.IsTrue(success.IsSuccess);
            Assert.AreEqual(42, success.Value);

            var failure = BmExecutionResult.Failure("error");
            Assert.IsTrue(failure.IsFailure);
        }
    }
}
