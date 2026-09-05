using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FunctionStorage;
using FunctionStorage.Framework;
using FunctionStorage.Abstractions;
using WorkFlowMethod.Usage;
using WorkFlowMethod.Orchestration.Context.Models;
using CommandFramework.Abstractions;
using CommandFramework.Execution;
using Modules.Registry;
using TConfig;

namespace WorkFlowMethod.Tests.UsageTests
{
    [TestClass]
    [TestCategory("FunctionStorageRegistry")]
    public class FunctionStorageRegistryUsageTests
    {
        private FunctionRegistry? _sharedRegistry;
        private FunctionExecutor? _sharedExecutor;

        [TestInitialize]
        public void Setup()
        {
            WorkFlowMethodInitializer.Reset();

            _sharedRegistry = new FunctionRegistry();
            _sharedExecutor = new FunctionExecutor(_sharedRegistry);

            _sharedRegistry.Register("Add", (int a, int b) => a + b);
            _sharedRegistry.Register("Subtract", (int a, int b) => a - b);
            _sharedRegistry.Register("Multiply", (int a, int b) => a * b);
            _sharedRegistry.Register("Divide", (int a, int b) =>
                b != 0 ? a / b : throw new DivideByZeroException("除数不能为零"));

            WorkFlowMethodInitializer.Initialize(_sharedRegistry, _sharedExecutor);
        }


        [TestMethod]
        public void Initialize_ShouldRegisterMathFunctions()
        {

            Assert.IsTrue(FunctionStorageRegistry.Contains("Add"), "Add 函数应已注册");
            Assert.IsTrue(FunctionStorageRegistry.Contains("Subtract"), "Subtract 函数应已注册");
            Assert.IsTrue(FunctionStorageRegistry.Contains("Multiply"), "Multiply 函数应已注册");
            Assert.IsTrue(FunctionStorageRegistry.Contains("Divide"), "Divide 函数应已注册");
        }

        [TestMethod]
        public void Initialize_ShouldSetWorkFlowMethodUsageProperties()
        {

            Assert.IsNotNull(WorkFlowMethodUsage.WorkFlowRegistry);
            Assert.IsNotNull(WorkFlowMethodUsage.Pipeline);
            Assert.IsNotNull(WorkFlowMethodUsage.Executor);
            Assert.IsTrue(WorkFlowMethodInitializer.IsInitialized);
        }


        [TestMethod]
        public async Task ExecuteMathFunction_ThroughWorkFlowMethodUsage()
        {

            var result = await WorkFlowMethodUsage.ExecuteFunctionAsync(
                "Add", new object[] { 3, 5 });

            Assert.IsTrue(result.IsSuccess, $"执行失败: {result.ErrorMessage}");
            Assert.AreEqual(8, Convert.ToInt32(result.Value));
        }

        [TestMethod]
        public async Task ExecuteSubtractFunction_ThroughWorkFlowMethodUsage()
        {
            var result = await WorkFlowMethodUsage.ExecuteFunctionAsync(
                "Subtract", new object[] { 10, 3 });

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(7, Convert.ToInt32(result.Value));
        }

        [TestMethod]
        public async Task ExecuteMultiplyFunction_ThroughWorkFlowMethodUsage()
        {
            var result = await WorkFlowMethodUsage.ExecuteFunctionAsync(
                "Multiply", new object[] { 4, 5 });

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(20, Convert.ToInt32(result.Value));
        }


        [TestMethod]
        public async Task TwoStepWorkflow_WithParameterPassing()
        {
            var step1Result = await WorkFlowMethodUsage.ExecuteFunctionAsync(
                "Add", new object[] { 3, 5 });
            Assert.IsTrue(step1Result.IsSuccess, $"Step 1 失败: {step1Result.ErrorMessage}");
            var step1Value = Convert.ToInt32(step1Result.Value);
            Assert.AreEqual(8, step1Value);

            WorkFlowMethodUsage.RegisterFunction("Double", ctx =>
            {
                var input = Convert.ToInt32(ctx.Parameters["input"]);
                return input * 2;
            });

            var step2Context = new WorkflowContext(
                parameters: new Dictionary<string, object> { { "input", step1Value } },
                executionId: "wf_2step_001");

            var step2Result = WorkFlowMethodUsage.CallFunction("Double", step2Context);

            Assert.AreEqual(16, Convert.ToInt32(step2Result));
        }


        [TestMethod]
        public async Task RegisterDelegate_ThroughFunctionStorage_ExecuteThroughWorkFlowMethodUsage()
        {
            FunctionStorageRegistry.Register("Concat", (string a, string b) => a + b);

            var result = await WorkFlowMethodUsage.ExecuteFunctionAsync(
                "Concat", new object[] { "Hello", "World" });

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("HelloWorld", result.Value?.ToString());
        }


        [TestMethod]
        public void RegisterWorkflowFunction_AndCall_ThroughWorkFlowMethodUsage()
        {
            WorkFlowMethodUsage.RegisterFunction("Greet", ctx =>
                $"Hello, {ctx.Parameters["name"]}!");

            var context = new WorkflowContext(
                parameters: new Dictionary<string, object> { { "name", "Alice" } },
                executionId: "greet_001");

            var result = WorkFlowMethodUsage.CallFunction("Greet", context);

            Assert.AreEqual("Hello, Alice!", result);
        }

        [TestMethod]
        public async Task RegisterAsyncWorkflowFunction_AndCall_ThroughWorkFlowMethodUsage()
        {
            WorkFlowMethodUsage.RegisterAsyncFunction("AsyncGreet", async ctx =>
            {
                await Task.Delay(10);
                return $"Async Hello, {ctx.Parameters["name"]}!";
            });

            var context = new WorkflowContext(
                parameters: new Dictionary<string, object> { { "name", "Bob" } },
                executionId: "async_greet_001");

            var result = await WorkFlowMethodUsage.CallFunctionAsync("AsyncGreet", context);

            Assert.AreEqual("Async Hello, Bob!", result);
        }


        [TestMethod]
        public void SharedRegistry_CrossCall_ShouldWork()
        {
            FunctionStorageRegistry.Register("SharedFunc", (int x) => x * 10);

            Assert.IsTrue(FunctionStorageRegistry.Contains("SharedFunc"));
            Assert.IsTrue(WorkFlowMethodUsage.HasFunction("SharedFunc"));

            var names = WorkFlowMethodUsage.GetAllFunctionNames();
            Assert.IsTrue(names.Contains("SharedFunc"));
        }


        [TestMethod]
        public void Contains_AfterRegistration_ShouldReturnCorrectly()
        {
            Assert.IsTrue(FunctionStorageRegistry.Contains("Add"));

            Assert.IsFalse(FunctionStorageRegistry.Contains("NonExistentFunction_XYZ"));
            Assert.IsFalse(WorkFlowMethodUsage.HasFunction("NonExistentFunction_XYZ"));
        }


        [TestMethod]
        public async Task ThreeStepWorkflow_WithChainedParameters()
        {
            var step1 = await WorkFlowMethodUsage.ExecuteFunctionAsync(
                "Add", new object[] { 10, 20 });
            Assert.IsTrue(step1.IsSuccess);
            var v1 = Convert.ToInt32(step1.Value);
            Assert.AreEqual(30, v1);

            var step2 = await WorkFlowMethodUsage.ExecuteFunctionAsync(
                "Multiply", new object[] { v1, 2 });
            Assert.IsTrue(step2.IsSuccess);
            var v2 = Convert.ToInt32(step2.Value);
            Assert.AreEqual(60, v2);

            WorkFlowMethodUsage.RegisterFunction("FormatResult", ctx =>
            {
                var value = Convert.ToInt32(ctx.Parameters["value"]);
                return $"Result: {value}";
            });

            var step3Context = new WorkflowContext(
                parameters: new Dictionary<string, object> { { "value", v2 } },
                executionId: "wf_3step_001");

            var step3 = WorkFlowMethodUsage.CallFunction("FormatResult", step3Context);

            Assert.AreEqual("Result: 60", step3);
        }


        #region ---- A. 完整初始化链测试 ----

        [TestMethod]
        public void A1_FullInitializationChain_ShouldWireUpCorrectly()
        {

            var storageRegistry = FunctionStorageRegistry.Registry;
            var usageRegistry = WorkFlowMethodUsage.BasicRegistry;

            Assert.IsNotNull(storageRegistry, "FunctionStorageRegistry.Registry 不应为 null");
            Assert.IsNotNull(usageRegistry, "WorkFlowMethodUsage.BasicRegistry 不应为 null");
            Assert.AreSame(storageRegistry, usageRegistry,
                "FunctionStorageRegistry.Registry 和 WorkFlowMethodUsage.BasicRegistry 应是同一个实例");
        }

        [TestMethod]
        public void A2_Reset_And_Reinitialize_ShouldWork()
        {

            WorkFlowMethodInitializer.Reset();
            Assert.IsFalse(WorkFlowMethodInitializer.IsInitialized, "Reset 后 IsInitialized 应为 false");

            WorkFlowMethodInitializer.Initialize(_sharedRegistry, _sharedExecutor);

            Assert.IsTrue(WorkFlowMethodInitializer.IsInitialized, "重新初始化后 IsInitialized 应为 true");
            Assert.IsNotNull(WorkFlowMethodUsage.WorkFlowRegistry);
            Assert.IsNotNull(WorkFlowMethodUsage.Pipeline);
            Assert.IsNotNull(WorkFlowMethodUsage.Executor);
            Assert.IsTrue(FunctionStorageRegistry.Contains("Add"), "重新初始化后 Add 函数应仍存在");
        }

        [TestMethod]
        public void A3_Initialize_Twice_ShouldBeIdempotent()
        {
            var firstRegistry = WorkFlowMethodUsage.BasicRegistry;

            WorkFlowMethodInitializer.Initialize(_sharedRegistry, _sharedExecutor);

            Assert.AreSame(firstRegistry, WorkFlowMethodUsage.BasicRegistry,
                "重复 Initialize 后注册表实例应不变");
            Assert.IsTrue(WorkFlowMethodInitializer.IsInitialized);
        }

        #endregion


        #region ---- B. WorkflowMethodExecutionWorkflow 端到端测试 ----

        [TestMethod]
        public async Task B1_ExecuteWorkflow_WithVirtualStore_ShouldReadExecuteAndWriteBack()
        {
            var workflow = new WorkflowMethodExecutionWorkflow();
            var virtualStore = new Dictionary<string, string?>
            {
                ["num1"] = "10",
                ["num2"] = "20"
            };

            var request = new ParameterExecutionRequest
            {
                FunctionName = "Add",
                InputControls = "num1,num2",
                OutputTarget = "result",
                VirtualStore = virtualStore,
                IsGuiMode = false
            };

            var result = await workflow.ExecuteAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Success, $"执行失败: {result.ErrorMessage}");
            Assert.AreEqual(30, Convert.ToInt32(result.Value));

            Assert.IsTrue(virtualStore.ContainsKey("result"), "结果应写回 VirtualStore");
            Assert.AreEqual("30", virtualStore["result"]);
        }

        [TestMethod]
        public async Task B2_ExecuteWorkflow_WithoutInputControls_ShouldPassNullParameters()
        {
            FunctionStorageRegistry.Register("GetDefault", () => 42);

            var workflow = new WorkflowMethodExecutionWorkflow();
            var request = new ParameterExecutionRequest
            {
                FunctionName = "GetDefault",
                InputControls = null,
                VirtualStore = new Dictionary<string, string?>()
            };

            var result = await workflow.ExecuteAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Success, $"执行失败: {result.ErrorMessage}");
            Assert.AreEqual(42, Convert.ToInt32(result.Value));
        }

        [TestMethod]
        public async Task B3_ExecuteWorkflow_WhenFunctionNotFound_ShouldReturnFail()
        {
            var workflow = new WorkflowMethodExecutionWorkflow();
            var request = new ParameterExecutionRequest
            {
                FunctionName = "NonExistentFunction_XYZ",
                InputControls = null,
                VirtualStore = new Dictionary<string, string?>()
            };

            var result = await workflow.ExecuteAsync(request, CancellationToken.None);

            Assert.IsFalse(result.Success, "执行不存在的函数应返回失败");
            Assert.IsNotNull(result.ErrorMessage, "失败结果应包含错误信息");
        }

        [TestMethod]
        public async Task B4_ExecuteWorkflow_WithCancellationToken_ShouldHandleCancellation()
        {
            var workflow = new WorkflowMethodExecutionWorkflow();
            var request = new ParameterExecutionRequest
            {
                FunctionName = "Add",
                InputControls = "a,b",
                VirtualStore = new Dictionary<string, string?>
                {
                    ["a"] = "1",
                    ["b"] = "2"
                }
            };

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var result = await workflow.ExecuteAsync(request, cts.Token);

            Assert.IsFalse(result.Success, "取消后应返回失败");
        }

        [TestMethod]
        public async Task B5_ExecuteWorkflow_WithMultipleInputControls_ShouldParseCorrectly()
        {
            var workflow = new WorkflowMethodExecutionWorkflow();
            var virtualStore = new Dictionary<string, string?>
            {
                ["a"] = "100",
                ["b"] = "200",
                ["c"] = "300"
            };

            FunctionStorageRegistry.Register("SumThree", (int a, int b, int c) => a + b + c);

            var request = new ParameterExecutionRequest
            {
                FunctionName = "SumThree",
                InputControls = "a,b,c",
                OutputTarget = "sum",
                VirtualStore = virtualStore
            };

            var result = await workflow.ExecuteAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Success, $"执行失败: {result.ErrorMessage}");
            Assert.AreEqual(600, Convert.ToInt32(result.Value));
            Assert.AreEqual("600", virtualStore["sum"]);
        }

        #endregion


        #region ---- C. 错误处理与边界测试 ----

        [TestMethod]
        public async Task C1_ExecuteNonExistentFunction_ShouldReturnFailure()
        {
            var result = await WorkFlowMethodUsage.ExecuteFunctionAsync(
                "NonExistentFunction_XYZ", new object[] { 1, 2 });

            Assert.IsFalse(result.IsSuccess, "执行不存在的函数应返回失败");
            Assert.IsNotNull(result.ErrorMessage, "失败结果应包含错误信息");
        }

        [TestMethod]
        public void C2_RegisterFunction_WithEmptyName_ShouldThrow()
        {
            Assert.ThrowsException<ArgumentException>(() =>
                FunctionStorageRegistry.Register("", (int x) => x));
        }

        [TestMethod]
        public async Task C3_UnregisterFunction_ThenExecute_ShouldFail()
        {
            Assert.IsTrue(FunctionStorageRegistry.Contains("Add"), "Add 函数应已注册");

            FunctionStorageRegistry.Unregister("Add");

            Assert.IsFalse(FunctionStorageRegistry.Contains("Add"));

            var result = await WorkFlowMethodUsage.ExecuteFunctionAsync(
                "Add", new object[] { 1, 2 });
            Assert.IsFalse(result.IsSuccess, "注销后执行应失败");
        }

        [TestMethod]
        public async Task C4_ExecuteFunction_WithoutInitialization_ShouldAutoInit()
        {
            WorkFlowMethodInitializer.Reset();

            FunctionStorageRegistry.Register("TestFunc", (int x) => x * 2);

            var result = await WorkFlowMethodUsage.ExecuteFunctionAsync(
                "TestFunc", new object[] { 21 });

            Assert.IsTrue(result.IsSuccess, $"执行失败: {result.ErrorMessage}");
            Assert.AreEqual(42, Convert.ToInt32(result.Value));
        }

        #endregion


        #region ---- D. 并发与批量执行测试 ----

        [TestMethod]
        public async Task D1_ConcurrentExecution_MultipleFunctions_ShouldAllSucceed()
        {
            var tasks = new[]
            {
                WorkFlowMethodUsage.ExecuteFunctionAsync("Add", new object[] { 1, 2 }),
                WorkFlowMethodUsage.ExecuteFunctionAsync("Subtract", new object[] { 10, 3 }),
                WorkFlowMethodUsage.ExecuteFunctionAsync("Multiply", new object[] { 4, 5 }),
                WorkFlowMethodUsage.ExecuteFunctionAsync("Add", new object[] { 100, 200 }),
                WorkFlowMethodUsage.ExecuteFunctionAsync("Subtract", new object[] { 50, 25 })
            };

            var results = await Task.WhenAll(tasks);

            Assert.AreEqual(5, results.Length);
            for (int i = 0; i < results.Length; i++)
            {
                Assert.IsTrue(results[i].IsSuccess,
                    $"任务 {i} 执行失败: {results[i].ErrorMessage}");
            }

            Assert.AreEqual(3, Convert.ToInt32(results[0].Value));
            Assert.AreEqual(7, Convert.ToInt32(results[1].Value));
            Assert.AreEqual(20, Convert.ToInt32(results[2].Value));
            Assert.AreEqual(300, Convert.ToInt32(results[3].Value));
            Assert.AreEqual(25, Convert.ToInt32(results[4].Value));
        }

        [TestMethod]
        public async Task D2_BatchExecution_ThroughFunctionStorageRegistry_ShouldWork()
        {
            var results = await FunctionStorageRegistry.ExecuteBatchAsync(
                "Add", "Subtract", "Multiply");

            Assert.AreEqual(3, results.Length);
            Assert.IsTrue(results[0].IsSuccess, "Add 执行失败");
            Assert.IsTrue(results[1].IsSuccess, "Subtract 执行失败");
            Assert.IsTrue(results[2].IsSuccess, "Multiply 执行失败");
        }

        [TestMethod]
        public async Task D3_WorkflowContext_WithExecutionId_ShouldPreserveContext()
        {
            var capturedExecutionId = "";
            var capturedDataValue = "";

            WorkFlowMethodUsage.RegisterFunction("ContextTest", ctx =>
            {
                capturedExecutionId = ctx.ExecutionId;
                capturedDataValue = ctx.Data["key1"]?.ToString() ?? "";
                return $"OK: {ctx.Parameters["input"]}";
            });

            var context = new WorkflowContext(
                parameters: new Dictionary<string, object> { { "input", 42 } },
                data: new Dictionary<string, object> { { "key1", "hello" } },
                executionId: "ctx_test_001");

            var result = WorkFlowMethodUsage.CallFunction("ContextTest", context);

            Assert.AreEqual("OK: 42", result);
            Assert.AreEqual("ctx_test_001", capturedExecutionId, "ExecutionId 应正确传递");
            Assert.AreEqual("hello", capturedDataValue, "Data 应正确传递");
        }

        #endregion


        #region ---- E. Pipeline 管线执行测试 ----

        [TestMethod]
        public async Task E1_ExecuteSingleStepPipeline_ThroughWorkFlowMethodUsage()
        {
            var result = await WorkFlowMethodUsage.ExecuteStepAsync<Func<int, int, int>>(
                "AddStep", (a, b) => a + b, new object[] { 7, 8 });

            Assert.IsTrue(result.IsSuccess, $"执行失败: {result.ErrorMessage}");
            Assert.AreEqual(15, Convert.ToInt32(result.Value));
        }

        [TestMethod]
        public async Task E2_ExecuteMultiStepPipeline_ThroughWorkFlowMethodUsage()
        {
            var result = await WorkFlowMethodUsage.ExecuteStepsAsync<Func<int, int, int>>(
                ("Step1", (a, b) => a + b),
                ("Step2", (a, b) => a * b));

            Assert.IsTrue(result.IsSuccess, $"执行失败: {result.ErrorMessage}");
        }

        #endregion


        #region ---- F. CallBridgedFunction 统一入口测试 ----

        [TestMethod]
        public void F1_CallBridgedFunction_ShouldRouteToWorkflowFunction()
        {
            WorkFlowMethodUsage.RegisterFunction("WorkflowHello", ctx =>
                $"Hello {ctx.Parameters["name"]} from Workflow");

            var context = new WorkflowContext(
                parameters: new Dictionary<string, object> { { "name", "Alice" } },
                executionId: "bridge_001");

            var result = WorkFlowMethodUsage.CallBridgedFunction("WorkflowHello", context);

            Assert.AreEqual("Hello Alice from Workflow", result);
        }

        [TestMethod]
        public void F2_CallBridgedFunction_ShouldRouteToStorageFunction()
        {
            FunctionStorageRegistry.Register("BridgedAdd", (int a, int b) => a + b);

            var context = new WorkflowContext(
                parameters: new Dictionary<string, object> { { "a", 15 }, { "b", 25 } },
                executionId: "bridge_002");

            var result = WorkFlowMethodUsage.CallBridgedFunction("BridgedAdd", context);

            Assert.AreEqual(40, Convert.ToInt32(result));
        }

        [TestMethod]
        public async Task F3_CallBridgedFunctionAsync_ShouldWorkForBothTypes()
        {
            WorkFlowMethodUsage.RegisterAsyncFunction("AsyncWorkflowFunc", async ctx =>
            {
                await Task.Delay(5);
                return $"Async: {ctx.Parameters["value"]}";
            });

            var wfContext = new WorkflowContext(
                parameters: new Dictionary<string, object> { { "value", 99 } },
                executionId: "bridge_003");

            var wfResult = await WorkFlowMethodUsage.CallBridgedFunctionAsync(
                "AsyncWorkflowFunc", wfContext);

            Assert.AreEqual("Async: 99", wfResult);
        }

        #endregion


        #region ---- G. IFunctionUnit 注册与执行测试 ----

        [TestMethod]
        public async Task G1_RegisterIFunctionUnit_ThroughFunctionStorage_ExecuteThroughWorkFlowMethodUsage()
        {
            var unit = new TestFunctionUnit("Square", "计算平方", typeof(int), typeof(int));
            FunctionStorageRegistry.Register(unit);

            var result = await WorkFlowMethodUsage.ExecuteFunctionAsync(
                "Square", new object[] { 7 });

            Assert.IsTrue(result.IsSuccess, $"执行失败: {result.ErrorMessage}");
            Assert.AreEqual(49, Convert.ToInt32(result.Value));
        }

        #endregion


        #region ---- H. 框架反转验证 ----

        [TestMethod]
        public void H1_RegistryInstanceSharing_ShouldBeSameReference()
        {

            var storageRegistry = FunctionStorageRegistry.Registry;
            var usageBasicRegistry = WorkFlowMethodUsage.BasicRegistry;

            Assert.AreSame(storageRegistry, usageBasicRegistry,
                "两个注册表应是同一个实例，证明框架反转的共享注册表机制正常工作");
        }

        [TestMethod]
        public void H2_DynamicRegistration_ShouldBeVisibleToBothSides()
        {
            FunctionStorageRegistry.Register("DynamicFunc", (string s) => s.ToUpper());

            Assert.IsTrue(WorkFlowMethodUsage.HasFunction("DynamicFunc"),
                "WorkFlowMethodUsage 应能感知 FunctionStorageRegistry 的动态注册");

            Assert.IsTrue(FunctionStorageRegistry.Contains("DynamicFunc"),
                "FunctionStorageRegistry 自身也应包含新注册的函数");
        }

        #endregion
    }


    public class TestFunctionUnit : FunctionStorage.Abstractions.IFunctionUnit
    {
        public string Name { get; }
        public string? Description { get; }
        public Type InputType { get; }
        public Type OutputType { get; }
        public bool IsAsync => false;

        public TestFunctionUnit(string name, string? description, Type inputType, Type outputType)
        {
            Name = name;
            Description = description;
            InputType = inputType;
            OutputType = outputType;
        }

        public Task<TConfig.ExecutionResult> ExecuteAsync(object? input)
        {
            if (input is int x)
            {
                var result = x * x;
                return Task.FromResult(TConfig.ExecutionResult.Success(result));
            }
            return Task.FromResult(TConfig.ExecutionResult.Failure("输入类型错误"));
        }
    }
}
