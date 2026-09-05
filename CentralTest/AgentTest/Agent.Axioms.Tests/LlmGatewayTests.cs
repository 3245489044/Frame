using System;
using Agent.Contract.Llm;
using Agent.State;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Agent.Axioms.Tests
{
    /// <summary>
    /// 统一 LLM 网关唯一咽喉（LlmGateway）测试 —— 覆盖方案 M1 验收：
    /// ① 经 <see cref="LlmGateway.Current"/> 的调用命中本地后端并计入 CallLog（GUI「CallLog>0」的进程内等价断言）；
    /// ② 未装配时 fail-fast 抛错（杜绝静默回退真实后端旁路）。
    /// </summary>
    [TestClass]
    public class LlmGatewayTests
    {
        [TestMethod]
        public void LlmGateway_Configured_RoutesToLocal_CountsCallLog()
        {
            try
            {
                var local = new LocalLlmClient(new LocalLlmOptions
                {
                    DefaultResponse = "（本地后端）已处理问题：{user}",
                    EchoInput = true,
                });
                // isLocalOnly=true → 路由到本地后端；resolveDefault 永不触达。
                var routing = new LlmRoutingClient(local, () => local, () => true);
                LlmMode.Configure(routing);

                Assert.IsTrue(LlmGateway.IsConfigured, "装配后 IsConfigured 应为 true");
                Assert.AreSame(routing, LlmGateway.Current, "Current 应为装配的路由代理");

                var before = LlmMode.CallLog.Count;
                var result = LlmGateway.Current.ChatAsync("system 提示", "GUI 测试问题").GetAwaiter().GetResult();

                Assert.AreEqual(before + 1, LlmMode.CallLog.Count, "经统一网关的调用应计入 CallLog（CallLog>0 的进程内等价断言）");
                Assert.IsFalse(string.IsNullOrWhiteSpace(result.Content), "本地后端应返回非空文本（终答非空的等价断言）");
            }
            finally
            {
                LlmMode.Configure(null); // 还原未装配，避免污染其他测试
            }
        }

        [TestMethod]
        public void LlmGateway_NotConfigured_Throws()
        {
            try
            {
                LlmMode.Configure(null); // 明确未装配

                try
                {
                    _ = LlmGateway.Current;
                    Assert.Fail("未装配时应抛 InvalidOperationException，而非静默返回");
                }
                catch (InvalidOperationException ex)
                {
                    StringAssert.Contains(ex.Message, "统一 LLM 网关");
                }
            }
            finally
            {
                LlmMode.Configure(null);
            }
        }
    }
}
