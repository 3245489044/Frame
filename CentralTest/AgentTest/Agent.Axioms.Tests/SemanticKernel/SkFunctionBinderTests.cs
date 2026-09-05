using System;
using System.Threading.Tasks;
using Agent.Axioms.SemanticKernel.Functions;
using Microsoft.SemanticKernel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Agent.Axioms.Tests.SemanticKernel
{
    /// <summary>
    /// SK 工具集注册守卫冒烟测试：验证引擎执行链函数（cline_execute/rule_match/guard_* 等）被拒绝进 SK 工具集，
    /// 而 LLM 辅助工具（白名单外）可正常注册并可经 kernel.InvokeAsync 调用。固化"两套注册表、两类函数、不得桥接"边界。
    /// </summary>
    [TestClass]
    public sealed class SkFunctionBinderTests
    {
        [TestMethod]
        public void BindLlmTool_EngineSkeletonName_Throws()
        {
            string[] engineNames = { "cline_execute", "rule_match", "guard_input", "guard_output", "provide_prompt" };
            foreach (var name in engineNames)
            {
                Assert.ThrowsExactly<NotSupportedException>(
                    () => SkFunctionBinder.BindLlmTool(name, "desc", (Func<string>)(() => "x")));
            }
        }

        [TestMethod]
        public async Task BindLlmTool_AssistantName_ReturnsInvokableFunction()
        {
            var fn = SkFunctionBinder.BindLlmTool(
                "query_available_strategies", "查询当前可用策略集", (Func<string>)(() => "ok"));
            var kernel = Kernel.CreateBuilder().Build();
            var result = await kernel.InvokeAsync(fn);
            Assert.AreEqual("ok", result.GetValue<string>());
        }
    }
}
