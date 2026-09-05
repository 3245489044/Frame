using System.Linq;
using System.Threading.Tasks;
using Agent.Contract.Llm;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Agent.Core.Tests
{
    /// <summary>
    /// LocalLlm「返回结果可选择可配置」新增能力单测 —— Mode=tool_call：
    /// 当调用方经 ChatWithToolsAsync 传入 tools 且命中 tool_call 规则时，返回一次确定性工具调用
    /// （LlmResult.ToolCalls，Content=null），供真实模拟基于 LLM 的 Function Calling / MCP 调用；
    /// 工具未提供 / 走纯聊天通道时不产生悬空工具调用，回退默认文本（零回归）。
    /// </summary>
    [TestClass]
    public class LocalLlmToolCallTests
    {
        private static LocalLlmOptions ToolCallOptions(string match, string toolName, string? args)
            => new()
            {
                DefaultResponse = "默认文本（未命中工具）",
                Rules = new()
                {
                    new LocalLlmRule
                    {
                        Match = match,
                        Mode = "tool_call",
                        ToolName = toolName,
                        ToolArguments = args,
                    },
                },
            };

        private static LlmTool CreateNodeReferenceTool()
            => new("create_node_reference", "按 CreateTaskGraphRequest 创建任务（确定性写图）",
                _ => Task.FromResult("{\"status\":\"ok\"}"));

        [TestMethod]
        public void ChatWithTools_WhenRuleMatchesAndToolProvided_ReturnsToolCall()
        {
            var client = new LocalLlmClient(ToolCallOptions("regex:创建任务", "create_node_reference", "{\"TaskKey\":\"t1\"}"));
            var tools = new[] { CreateNodeReferenceTool() };

            var r = client.ChatWithToolsAsync("sys", "请创建任务 t1", tools).GetAwaiter().GetResult();

            Assert.IsTrue(r.IsSuccess, $"应成功，Error={r.Error}");
            Assert.IsNull(r.Content, "返回工具调用时 Content 应为 null");
            Assert.IsNotNull(r.ToolCalls, "命中 tool_call 规则应返回 ToolCalls");
            Assert.AreEqual(1, r.ToolCalls!.Count);
            Assert.AreEqual("create_node_reference", r.ToolCalls[0].Name);
            Assert.AreEqual("{\"TaskKey\":\"t1\"}", r.ToolCalls[0].Arguments);
        }

        [TestMethod]
        public void ChatWithTools_WhenToolNotProvided_FallsBackToDefaultText()
        {
            var client = new LocalLlmClient(ToolCallOptions("创建任务", "create_node_reference", "{}"));

            var r = client.ChatWithToolsAsync("sys", "请创建任务 t1", tools: null).GetAwaiter().GetResult();

            Assert.IsTrue(r.IsSuccess);
            Assert.IsNull(r.ToolCalls, "未提供工具时不得产生悬空工具调用");
            Assert.AreEqual("默认文本（未命中工具）", r.Content);
        }

        [TestMethod]
        public void ChatWithTools_WhenToolNameMismatch_FallsBackToDefaultText()
        {
            var client = new LocalLlmClient(ToolCallOptions("创建任务", "create_node_reference", "{}"));
            var tools = new[] { new LlmTool("other_tool", "desc", _ => Task.FromResult("{}")) };

            var r = client.ChatWithToolsAsync("sys", "请创建任务 t1", tools).GetAwaiter().GetResult();

            Assert.IsNull(r.ToolCalls, "工具名不匹配时不得产生工具调用");
            Assert.AreEqual("默认文本（未命中工具）", r.Content);
        }

        [TestMethod]
        public void ChatAsync_WithoutTools_NeverEmitsToolCall()
        {
            var client = new LocalLlmClient(ToolCallOptions("创建任务", "create_node_reference", "{}"));

            var r = client.ChatAsync("sys", "请创建任务 t1").GetAwaiter().GetResult();

            Assert.IsTrue(r.IsSuccess);
            Assert.IsNull(r.ToolCalls, "纯聊天通道（未带 tools）不得触发工具调用");
            Assert.AreEqual("默认文本（未命中工具）", r.Content);
        }

        [TestMethod]
        public void CallLog_RecordsToolCall_ForAudit()
        {
            var client = new LocalLlmClient(ToolCallOptions("创建任务", "create_node_reference", "{\"TaskKey\":\"t1\"}"));
            var tools = new[] { CreateNodeReferenceTool() };

            client.ChatWithToolsAsync("sys", "请创建任务 t1", tools).GetAwaiter().GetResult();

            var rec = client.CallLog.Last();
            Assert.IsNotNull(rec.ToolCalls, "CallLog 应记录工具调用（审计可观测）");
            Assert.AreEqual("create_node_reference", rec.ToolCalls![0].Name);
        }

        // ---- LlmToolExecutor（共享工具执行器）：LLM 决策返回的工具调用 → 真实模拟执行 ----

        [TestMethod]
        public void LlmToolExecutor_ExecutesReturnedToolCall_ReturnsResultText()
        {
            var client = new LocalLlmClient(ToolCallOptions("创建任务", "create_node_reference", "{\"TaskKey\":\"t1\"}"));
            var tool = new LlmTool("create_node_reference", "按 CreateTaskGraphRequest 创建任务（确定性写图）",
                args => Task.FromResult("{\"ok\":true,\"task\":\"t1\",\"echo\":" + args + "}"));

            var r = client.ChatWithToolsAsync("sys", "请创建任务 t1", new[] { tool }).GetAwaiter().GetResult();
            Assert.IsNotNull(r.ToolCalls, "LocalLlm 命中 tool_call 规则应返回工具调用");

            var text = LlmToolExecutor.ExecuteToolCallsAsync(r, new[] { tool }).GetAwaiter().GetResult();
            StringAssert.Contains(text, "create_node_reference");
            StringAssert.Contains(text, "\"ok\":true");
        }

        [TestMethod]
        public void LlmToolExecutor_NoToolCalls_ReturnsFinalContent()
        {
            var text = LlmToolExecutor.ExecuteToolCallsAsync(new LlmResult("最终文本", true), tools: null).GetAwaiter().GetResult();
            Assert.AreEqual("最终文本", text, "无工具调用时应原样返回最终文本（真实后端零回归）");
        }

        [TestMethod]
        public void LlmToolExecutor_ToolMissing_ReturnsDiagnosticNotThrow()
        {
            var r = new LlmResult(null, true, null, new[] { new LlmToolCall("ghost_tool", "{}") });
            var tool = new LlmTool("create_node_reference", "desc", _ => Task.FromResult("{}"));

            var text = LlmToolExecutor.ExecuteToolCallsAsync(r, new[] { tool }).GetAwaiter().GetResult();

            StringAssert.Contains(text, "工具不存在");
        }
    }
}
