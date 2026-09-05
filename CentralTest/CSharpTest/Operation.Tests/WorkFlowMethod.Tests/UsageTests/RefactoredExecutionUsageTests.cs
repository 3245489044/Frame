using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkFlowMethod.Orchestration.Refactored;
using WorkFlowMethod.Skeletons.NodeProcess.Models;

namespace WorkFlowMethod.Tests.UsageTests
{
    [TestClass]
    [TestCategory("RefactoredExecution")]
    public class RefactoredExecutionUsageTests
    {
        [TestMethod]
        public void Executor_Create_ShouldNotBeNull()
        {
            using var executor = new RefactoredWorkflowExecutor();
            Assert.IsNotNull(executor);
        }

        [TestMethod]
        public void ModuleCreationConfig_Constructor_ShouldSetProperties()
        {
            var config = new ModuleCreationConfig("wf_001", "TestSymbol", 2, "{}");
            Assert.AreEqual("wf_001", config.WorkflowId);
            Assert.AreEqual("TestSymbol", config.Symbol);
            Assert.AreEqual(2, config.ChainCount);
            Assert.AreEqual("{}", config.JsonConfig);
            Assert.IsTrue(config.EnableValidation);
        }

        [TestMethod]
        public void ModuleCreationConfig_NullArgs_ShouldThrow()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new ModuleCreationConfig(null, "sym", 1));
            Assert.ThrowsException<ArgumentNullException>(() => new ModuleCreationConfig("id", null, 1));
            Assert.ThrowsException<ArgumentException>(() => new ModuleCreationConfig("id", "sym", 0));
        }

        [TestMethod]
        public void ModuleCreationResult_CreateSuccessAndFailure_ShouldSetProperties()
        {
            var success = ModuleCreationResult.CreateSuccess("wf_001",
                new System.Collections.Generic.List<string> { "chain_001" },
                new System.Collections.Generic.List<string> { "node_001" });
            Assert.IsTrue(success.Success);
            Assert.AreEqual("wf_001", success.WorkflowId);
            Assert.AreEqual(1, success.ChainIds.Count);
            Assert.AreEqual(1, success.NodeKeys.Count);

            var failure = ModuleCreationResult.CreateFailure("wf_001", "参数无效");
            Assert.IsFalse(failure.Success);
            Assert.AreEqual("参数无效", failure.ErrorMessage);
        }

        [TestMethod]
        public async Task DefaultModuleCreator_CreateModuleAsync_ShouldWork()
        {
            var creator = new DefaultModuleCreator();
            var config = new ModuleCreationConfig("wf_002", "DynamicModule", 1);
            var result = await creator.CreateModuleAsync(config);
            Assert.IsTrue(result.Success);
            Assert.IsNotNull(result.ChainIds);
        }

        [TestMethod]
        public void ModuleCreationManager_RegisterAndInitialize_ShouldWork()
        {
            var manager = new ModuleCreationManager();
            manager.RegisterPendingCreation(new ModuleCreationConfig("wf_003", "ModuleA", 1));
            manager.RegisterPendingCreation(new ModuleCreationConfig("wf_004", "ModuleB", 2));

            Assert.IsFalse(manager.IsInitialized);

            var results = manager.InitializeAsync().GetAwaiter().GetResult();
            Assert.IsTrue(manager.IsInitialized);
            Assert.AreEqual(2, results.Count);

            Assert.ThrowsException<InvalidOperationException>(() =>
                manager.RegisterPendingCreation(new ModuleCreationConfig("wf_005", "Late", 1)));
            Assert.ThrowsException<InvalidOperationException>(() =>
                manager.InitializeAsync().GetAwaiter().GetResult());
        }

        [TestMethod]
        public void SimpleExceptionHandler_ShouldHandleWithoutThrow()
        {
            var handler = new SimpleExceptionHandler();
            handler.HandleException(new InvalidOperationException("测试异常"), "exec_001", "node_001");
        }

        [TestMethod]
        public async Task ModuleCreationExtensions_CreateModuleSafeAsync_ShouldHandle()
        {
            var creator = new DefaultModuleCreator();
            var config = new ModuleCreationConfig("wf_006", "SafeModule", 1);
            var result = await creator.CreateModuleSafeAsync(config);
            Assert.IsTrue(result.Success);
        }
    }
}
