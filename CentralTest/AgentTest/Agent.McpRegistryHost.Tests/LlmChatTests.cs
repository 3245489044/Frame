using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Agent.Contract.Llm;
using Agent.Modules;

namespace Agent.McpRegistryHost.Tests
{
    /// <summary>
    /// LlmChat（llm_chat 统一 LLM 出口纯封装）单元测试。全部零真实 LLM（假 ILlmClient），可在纯 CI 跑。
    /// 验证：纯聊天路径 / 带 toolsJson（N1 合一）路径 / conversationContext 前置 / 失败返回结构化错误 JSON / 非法 toolsJson 回退纯聊天。
    /// </summary>
    [TestClass]
    public class LlmChatTests
    {
        private sealed class FakeLlmClient : ILlmClient
        {
            public string Name => "fake";
            public string? LastSystem;
            public string? LastUser;
            public IReadOnlyList<LlmTool>? LastTools;
            public bool UsedTools;
            public LlmResult Reply = new("默认回复");
            // 工具路径的独立回复（仅测试设值覆盖；不设则复用 Reply）。CS0649 免除：初值赋空具象，不视为从未赋值。
            public LlmResult? ToolsReply = null;

            public Task<LlmResult> ChatAsync(
                string system, string user, IReadOnlyList<LlmMessage>? history = null, CancellationToken ct = default)
            {
                UsedTools = false;
                LastSystem = system;
                LastUser = user;
                return Task.FromResult(Reply);
            }

            public Task<LlmResult> ChatWithToolsAsync(
                string system, string user, IReadOnlyList<LlmTool>? tools = null,
                IReadOnlyList<LlmMessage>? history = null, CancellationToken ct = default)
            {
                UsedTools = true;
                LastSystem = system;
                LastUser = user;
                LastTools = tools;
                return Task.FromResult(ToolsReply ?? Reply);
            }
        }

        [TestMethod]
        public async Task ExecuteAsync_NoTools_UsesPureChat_ReturnsText()
        {
            var llm = new FakeLlmClient { Reply = new LlmResult("纯聊天回复") };
            var text = await LlmChat.ExecuteAsync(llm, "你好", system: "sys", ct: default);
            Assert.IsFalse(llm.UsedTools);
            Assert.AreEqual("sys", llm.LastSystem);
            Assert.AreEqual("你好", llm.LastUser);
            Assert.AreEqual("纯聊天回复", text);
        }

        [TestMethod]
        public async Task ExecuteAsync_WithToolsJson_UsesToolChat_AndKeepsOriginalFinalText()
        {
            var llm = new FakeLlmClient { Reply = new LlmResult("带工具聊天回复") };
            var toolsJson = "[{\"name\":\"create_node_reference\",\"description\":\"建任务\",\"inputSchema\":{\"type\":\"object\"}}]";
            var text = await LlmChat.ExecuteAsync(llm, "请建任务", system: "sys", toolsJson: toolsJson, ct: default);
            Assert.IsTrue(llm.UsedTools);
            Assert.IsNotNull(llm.LastTools);
            Assert.AreEqual(1, llm.LastTools!.Count);
            Assert.AreEqual("create_node_reference", llm.LastTools![0].Name);
            // 纯封装只返回 LLM 决策文本（工具执行留在框架侧），不吞掉最终文本。
            Assert.AreEqual("带工具聊天回复", text);
        }

        [TestMethod]
        public async Task ExecuteAsync_ConversationContext_PrependedToUser()
        {
            var llm = new FakeLlmClient { Reply = new LlmResult("ok") };
            await LlmChat.ExecuteAsync(llm, "继续", system: "sys", conversationContext: "[{\"role\":\"user\",\"content\":\"上轮\"}]", ct: default);
            Assert.IsNotNull(llm.LastUser);
            StringAssert.Contains(llm.LastUser, "上轮");
            StringAssert.Contains(llm.LastUser, "继续");
            Assert.IsTrue(llm.LastUser!.IndexOf("上轮", StringComparison.Ordinal)
                          < llm.LastUser!.IndexOf("继续", StringComparison.Ordinal));
        }

        [TestMethod]
        public async Task ExecuteAsync_Failure_ReturnsStructuredErrorJson()
        {
            var llm = new FakeLlmClient { Reply = LlmResult.Fail("触网失败") };
            var text = await LlmChat.ExecuteAsync(llm, "x", system: "sys", ct: default);
            StringAssert.Contains(text, "\"status\":\"error\"");
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            Assert.AreEqual("error", doc.RootElement.GetProperty("status").GetString());
            // message 可能是转义形式，经 JSON 解析取回原文后断言。
            Assert.AreEqual("触网失败", doc.RootElement.GetProperty("message").GetString());
        }

        [TestMethod]
        public async Task ExecuteAsync_InvalidToolsJson_FallsBackToPureChat()
        {
            var llm = new FakeLlmClient { Reply = new LlmResult("回退聊天") };
            // 非法 JSON：整体捕获回退纯聊天（TryParseTools → null）。
            var text = await LlmChat.ExecuteAsync(llm, "x", system: "sys", toolsJson: "{broken", ct: default);
            Assert.IsFalse(llm.UsedTools);
            Assert.AreEqual("回退聊天", text);
        }

        [TestMethod]
        public async Task ExecuteAsync_WhitespaceOnlyToolsJson_FallsBackToPureChat()
        {
            var llm = new FakeLlmClient { Reply = new LlmResult("回退聊天") };
            var text = await LlmChat.ExecuteAsync(llm, "x", system: "sys", toolsJson: "   ", ct: default);
            Assert.IsFalse(llm.UsedTools);
            Assert.AreEqual("回退聊天", text);
        }
    }
}
