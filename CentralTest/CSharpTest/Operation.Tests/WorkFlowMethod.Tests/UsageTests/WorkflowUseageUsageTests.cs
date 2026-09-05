using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkFlowMethod.Usage;
using WorkFlowMethod.Orchestration.Context.Models;

namespace WorkFlowMethod.Tests.UsageTests
{
    [TestClass]
    [TestCategory("WorkFlowMethodUsage")]
    public class WorkflowUseageUsageTests
    {
        [TestInitialize]
        public void Setup() => WorkFlowMethodUsage.Reset();

        [TestMethod]
        public void Initialize_ShouldSetProperties()
        {
            WorkFlowMethodUsage.Initialize();
            Assert.IsNotNull(WorkFlowMethodUsage.WorkFlowRegistry);
            Assert.IsNotNull(WorkFlowMethodUsage.Pipeline);
            Assert.IsNotNull(WorkFlowMethodUsage.Executor);
        }

        [TestMethod]
        public void RegisterAndCallFunction_ShouldRoundtrip()
        {
            WorkFlowMethodUsage.Initialize();
            WorkFlowMethodUsage.RegisterFunction("TestFunc", ctx => "Hello " + ctx.ExecutionId);
            var result = WorkFlowMethodUsage.CallFunction("TestFunc", new WorkflowContext(executionId: "exec_001"));
            Assert.AreEqual("Hello exec_001", result);
        }

        [TestMethod]
        public async Task RegisterAndCallAsyncFunction_ShouldRoundtrip()
        {
            WorkFlowMethodUsage.Initialize();
            WorkFlowMethodUsage.RegisterAsyncFunction("AsyncFunc", async ctx =>
            {
                await Task.Delay(10);
                return "Async_" + ctx.ExecutionId;
            });
            var result = await WorkFlowMethodUsage.CallFunctionAsync("AsyncFunc", new WorkflowContext(executionId: "exec_002"));
            Assert.AreEqual("Async_exec_002", result);
        }

        [TestMethod]
        public void HasFunction_ShouldReturnCorrectly()
        {
            WorkFlowMethodUsage.Initialize();
            WorkFlowMethodUsage.RegisterFunction("ExistsFunc", ctx => "ok");
            Assert.IsTrue(WorkFlowMethodUsage.HasFunction("ExistsFunc"));
            Assert.IsFalse(WorkFlowMethodUsage.HasFunction("NonExistent"));
        }

        [TestMethod]
        public void CreatePipeline_ShouldReturnBuilder()
        {
            WorkFlowMethodUsage.Initialize();
            Assert.IsNotNull(WorkFlowMethodUsage.CreatePipeline("TestPipeline"));
        }

        [TestMethod]
        public void LoadPipelineFromJson_Valid_ShouldReturnDefinition()
        {
            WorkFlowMethodUsage.Initialize();
            var json = "{\"Name\":\"TestPipeline\",\"Steps\":[{\"Name\":\"step1\",\"Function\":\"test\"}]}";
            Assert.IsNotNull(WorkFlowMethodUsage.LoadPipelineFromJson(json));
        }

        [TestMethod]
        public void LoadPipelineFromJson_Invalid_ShouldReturnNull()
        {
            WorkFlowMethodUsage.Initialize();
            Assert.IsNull(WorkFlowMethodUsage.LoadPipelineFromJson("invalid json"));
        }
    }
}
