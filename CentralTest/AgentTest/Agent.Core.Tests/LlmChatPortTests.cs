using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Agent.Contract.Llm;
using Agent.Core.Signal;

namespace Agent.Core.Tests
{
    /// <summary>
    /// ILlmChatPort / LlmClientChatPort（P1·去 _llm 收敛点）单元测试：端口把带/不带工具的 LLM 调用转发到 ILlmClient，
    /// 完整保留 Function Calling 往返（含 ToolCalls）；折叠算子/answer 经端口而非直持 ILlmClient。零真实 LLM。
    /// </summary>
    [TestClass]
    public class LlmChatPortTests
    {
        private sealed class FakeLlmClient : ILlmClient
        {
            public string Name => "fake";
            public string? LastSystem;
            public string? LastUser;
            public IReadOnlyList<LlmTool>? LastTools;
            public bool UsedTools;
            public LlmResult? Reply;

            public Task<LlmResult> ChatAsync(
                string system, string user, IReadOnlyList<LlmMessage>? history = null, CancellationToken ct = default)
            {
                UsedTools = false;
                LastSystem = system;
                LastUser = user;
                return Task.FromResult(Reply ?? new LlmResult("纯聊天"));
            }

            public Task<LlmResult> ChatWithToolsAsync(
                string system, string user, IReadOnlyList<LlmTool>? tools = null,
                IReadOnlyList<LlmMessage>? history = null, CancellationToken ct = default)
            {
                UsedTools = true;
                LastSystem = system;
                LastUser = user;
                LastTools = tools;
                return Task.FromResult(Reply ?? new LlmResult("带工具聊天"));
            }
        }

        [TestMethod]
        public async Task Port_NoTools_ForwardsToPureChat()
        {
            var fake = new FakeLlmClient();
            var port = new LlmClientChatPort(fake);
            var r = await port.ChatAsync("sys", "你好", ct: default);
            Assert.IsFalse(fake.UsedTools);
            Assert.AreEqual("sys", fake.LastSystem);
            Assert.AreEqual("你好", fake.LastUser);
            Assert.AreEqual("纯聊天", r.Content);
        }

        [TestMethod]
        public async Task Port_WithTools_ForwardsToToolChat_AndKeepsToolCalls()
        {
            var fake = new FakeLlmClient
            {
                Reply = new LlmResult(Content: null,
                    ToolCalls: new[] { new LlmToolCall("seq_think", "{}") }),
            };
            var port = new LlmClientChatPort(fake);
            var tool = new LlmTool("seq_think", "思考", _ => Task.FromResult("{\"ok\":true}"));
            var r = await port.ChatAsync("sys", "请思考", new[] { tool }, ct: default);
            // 端口保留结构化 ToolCalls（折叠算子据此经 LlmToolExecutor 执行回填），不吞为纯文本。
            Assert.IsTrue(fake.UsedTools);
            Assert.IsNotNull(fake.LastTools);
            Assert.AreEqual(1, fake.LastTools!.Count);
            Assert.IsNotNull(r.ToolCalls);
            Assert.AreEqual("seq_think", r.ToolCalls![0].Name);
        }
    }
}
