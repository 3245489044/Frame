using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TConfig;
using ExecutionFramework.Engine;
using ExecutionFramework.Registry;
using WFRegistry = WorkFlowMethod.FunctionExecutionFramework.Core.DefaultWorkflowRegistry;
using WfContext = WorkFlowMethod.Orchestration.Context.Models.WorkflowContext;

namespace Agent.Core.Tests
{
    /// <summary>
    /// 8.23「节点端口空 → 终答空」根因决定性验证 —— 完整模拟 GUI 的注册-调用链：
    /// RegisterAsyncFunction 包装(async FunctionContext → WfContext) → FunctionRegistry.Register
    /// → DefaultWorkflowRegistry.CallAsync(WfContext)。若此处返回 null，即复现 CallFunctionAsync 返回 null
    /// → ExtractProps 空 → 节点端口空 → 终答空的完整根因链。
    /// </summary>
    [TestClass]
    public class CallAsyncRegressionTests
    {
        [TestMethod]
        public async Task DefaultWorkflowRegistry_AsyncWfContextHandler_ReturnsResult()
        {
            var reg = new WFRegistry();
            Func<WfContext, Task<object>> handler = async ctx => { await Task.Yield(); return "hello"; };
            // 模拟 WorkFlowMethodUsage.RegisterAsyncFunction 的包装委托
            reg.Register("test", (Delegate)(async (FunctionContext ctx) =>
            {
                var wfCtx = new WfContext(
                    parameters: ctx.Parameters,
                    data: ctx.Items,
                    executionId: ctx.ExecutionId,
                    nodeKey: null,
                    workflowId: null);
                return await handler(wfCtx).ConfigureAwait(false);
            }));

            var callArgs = new WfContext(
                parameters: new Dictionary<string, object>(),
                data: new Dictionary<string, object>(),
                executionId: "e", nodeKey: "n", workflowId: "w");

            var result = await reg.CallAsync("test", callArgs);

            Assert.AreEqual("hello", result,
                $"DefaultWorkflowRegistry.CallAsync 应返回异步委托结果（若为 null 即复现 GUI 根因），实际={result?.GetType().Name ?? "null"}");
        }

        /// <summary>同步委托（非 Task）应原样返回值，不被解包逻辑吞掉。</summary>
        [TestMethod]
        public async Task DefaultWorkflowRegistry_SyncHandler_ReturnsValue()
        {
            var reg = new WFRegistry();
            reg.Register("sync", (Delegate)((FunctionContext ctx) => "sync-ok"));

            var result = await reg.CallAsync("sync", new WfContext());

            Assert.AreEqual("sync-ok", result);
        }

        /// <summary>非泛型 async Task（无结果值）是合法语义 → 返回 null，不视为异常。</summary>
        [TestMethod]
        public async Task DefaultWorkflowRegistry_NonGenericAsyncTaskHandler_ReturnsNull()
        {
            var reg = new WFRegistry();
            reg.Register("task", (Delegate)(async (FunctionContext ctx) => { await Task.Yield(); }));

            var result = await reg.CallAsync("task", new WfContext());

            Assert.IsNull(result, "非泛型 async Task 无结果值，应返回 null（合法语义，非吞值）");
        }

        /// <summary>async Task&lt;T&gt; 委托抛出异常应向上传播（原始异常，非静默 null）。</summary>
        [TestMethod]
        public async Task DefaultWorkflowRegistry_AsyncHandlerThrows_PropagatesOriginal()
        {
            var reg = new WFRegistry();
            reg.Register("boom", (Delegate)(async (FunctionContext ctx) =>
            {
                await Task.Yield();
                throw new InvalidOperationException("boom");
            }));

            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await reg.CallAsync("boom", new WfContext()));
        }

        /// <summary>双注册表行为一致性：同一 async 委托经 DefaultWorkflowRegistry 与 OverridableFunctionRegistry 解包结果应相同。</summary>
        [TestMethod]
        public async Task BothRegistries_AsyncHandler_UnwrapConsistent()
        {
            var defaultReg = new WFRegistry();
            var overrideReg = new OverridableFunctionRegistry(new TrConfigRegistry());

            Func<WfContext, Task<object>> handler = async ctx => { await Task.Yield(); return "consistent"; };
            Delegate wrapper = (async (FunctionContext ctx) =>
            {
                var wfCtx = new WfContext(
                    parameters: ctx.Parameters, data: ctx.Items,
                    executionId: ctx.ExecutionId, nodeKey: null, workflowId: null);
                return await handler(wfCtx).ConfigureAwait(false);
            });

            defaultReg.Register("same", wrapper);
            overrideReg.Register("same", wrapper);

            var fromDefault = await defaultReg.CallAsync("same", new WfContext());
            var fromOverride = await overrideReg.CallAsync("same", new WfContext());

            Assert.AreEqual("consistent", fromDefault);
            Assert.AreEqual("consistent", fromOverride, "两注册表经统一 TaskHelper 解包结果应一致（TH-12 SingleImplementation）");
        }
    }
}
