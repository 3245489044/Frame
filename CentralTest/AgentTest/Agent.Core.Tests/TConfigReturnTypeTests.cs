using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TConfig;

namespace Agent.Core.Tests
{
    /// <summary>
    /// 8.23「节点端口空 → 终答空」根因验证 —— 验证 async 委托的 ReturnType 识别与 TaskHelper 展开。
    /// GUI 中 nodePorts=[] 说明 CallFunctionAsync 返回 null；TaskHelper.InvokeAndUnwrapAsync 在
    /// ReturnType==typeof(Task)（非泛型）时返回 null。本测试验证：异步委托 ReturnType 是否被正确识别为 Task&lt;object&gt;。
    /// </summary>
    [TestClass]
    public class TConfigReturnTypeTests
    {
        [TestMethod]
        public void FunctionDescriptor_AsyncObjectLambda_ReturnTypeIsGenericTask()
        {
            Func<object, Task<object>> fn = async ctx => { await Task.Yield(); return "hello"; };
            var desc = FunctionDescriptor.FromDelegate(fn);
            Assert.AreEqual(typeof(Task<object>), desc.ReturnType,
                $"async object 委托 ReturnType 应为 Task<object>，实际 {desc.ReturnType}");
            Assert.IsFalse(desc.ReturnType == typeof(Task),
                "ReturnType 不应被识别为非泛型 Task（否则 TaskHelper 返回 null → 节点端口空 → 终答空）");
        }

        [TestMethod]
        public async Task TaskHelper_GenericTaskType_UnwrapsResult()
        {
            Func<Task<object>> inner = async () => { await Task.Yield(); return "hello"; };
            Func<object?> invoker = () => inner();
            var result = await TaskHelper.InvokeAndUnwrapAsync(invoker, typeof(Task<object>));
            Assert.AreEqual("hello", result, "ReturnType=Task<object> 应展开到结果");
        }

        [TestMethod]
        public async Task TaskHelper_NonGenericTaskType_ReturnsNull()
        {
            // 根因机制复现：若 config.ReturnType 被识别为非泛型 Task，则即使实际返回 Task<object>，也返回 null。
            Func<Task<object>> inner = async () => { await Task.Yield(); return "hello"; };
            Func<object?> invoker = () => inner();
            var result = await TaskHelper.InvokeAndUnwrapAsync(invoker, typeof(Task));
            Assert.IsNull(result, "ReturnType=Task（非泛型）→ 返回 null，这正是 CallFunctionAsync 返回 null 的机制");
        }
    }
}
