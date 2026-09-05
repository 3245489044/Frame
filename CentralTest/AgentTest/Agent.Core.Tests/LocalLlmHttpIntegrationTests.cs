using System;
using System.Net;
using System.Net.Sockets;
using Agent.Contract;
using Agent.Contract.Llm;
using Agent.Modules;
using Agent.State;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestInfrastructure;

namespace Agent.Core.Tests
{
    /// <summary>
    /// 本地 LLM 完整链路端到端复现 —— 定位「本地调用返回空内容 → 会话终答 textLen=0」的框架断点。
    /// 链路：本地腿 OpenAiCompatibleLlmClient(8787) → LocalLlmHttpHost → LocalLlmClient（规则引擎）。
    /// 若此处返回空，即确认「本地腿拿不到内容」是 8.23 会话回复为空的直接根因。
    /// </summary>
    [TestClass]
    [TestCategory(TestCategories.Integration)]
    public class LocalLlmHttpIntegrationTests
    {
        private static int FindFreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        [TestMethod]
        public void LocalLlmClient_WithRealAgentConfig_ReturnsNonEmpty()
        {
            // 真实配置复现：用真实 agent-config.json 的 LocalLlm 节构造本地客户端，
            // 确认 DefaultResponse/Rules 加载正确、ChatMessagesAsync 返回非空（否则即 textLen=0 的配置侧根因）。
            var config = new Agent.Launcher.AgentConfig();
            var applied = config.ApplyExternalConfig();
            Assert.IsTrue(applied, "真实 agent-config.json 应成功加载（配置侧路径解析）；若失败即为配置路径/加载断点");
            var client = new LocalLlmClient(config.LocalLlm);
            var r = client.ChatMessagesAsync(new[]
            {
                LlmMessage.System("sys"),
                LlmMessage.User("GUI 测试：统一 LLM 网关本地路由，请用一句话回复"),
            }).GetAwaiter().GetResult();

            Assert.IsFalse(string.IsNullOrWhiteSpace(r.Content),
                $"真实配置下本地应返回非空内容（textLen=0 根因排查），实际 Content='{r.Content}'，DefaultResponse='{config.LocalLlm.DefaultResponse}'");
        }

        [TestMethod]
        public void LocalLeg_ReturnsNonEmptyContent_ThroughFullChain()
        {
            var port = FindFreePort();
            var prefix = $"http://127.0.0.1:{port}/";
            var llmOptions = new LocalLlmOptions
            {
                DefaultResponse = "（本地后端·LocalLlm）已处理问题：{user}",
                EchoInput = true,
                DelayMs = 30,
                Rules = new()
                {
                    new() { Match = "regex:介绍|能力|Cline", Mode = "text", Response = "Cline 核心能力：代码代理、工具调用、多轮循环。" },
                },
            };
            var serverLocal = new LocalLlmClient(llmOptions);
            var host = LocalLlmHttpModule.Start(
                new LocalLlmHttpOptions { Enabled = true, ListenPrefix = prefix, Model = "local-llm" }, serverLocal);
            try
            {
                var localLeg = new OpenAiCompatibleLlmClient(prefix, apiKey: null, "local-llm", name: "local-http");
                var routing = new LlmRoutingClient(localLeg, () => serverLocal, () => true);
                var r = routing.ChatAsync("sys", "GUI 测试：统一 LLM 网关本地路由，请用一句话回复").GetAwaiter().GetResult();

                Assert.IsTrue(r.IsSuccess, $"本地腿调用应成功，实际 Error={r.Error}");
                Assert.IsFalse(string.IsNullOrWhiteSpace(r.Content),
                    $"本地腿应返回非空内容（textLen=0 的直接根因排查），实际 Content='{r.Content}'");
            }
            finally
            {
                host.Dispose();
            }
        }

        [TestMethod]
        public void WireTools_LocalLlm_ReturnsToolCall_Executed_ThenFinalAnswer()
        {
            // Phase 2 闭环：带 tools 的 OpenAI 兼容客户端（OpenAiCompatibleLlmClient）经 8787 走完整 wire 工具循环：
            //   → 服务端 LocalLlmHttp 解析 tools → LocalLlmClient 命中 tool_call 规则返回调用
            //   → OpenAiCompatibleTransport 解析 tool_calls 并执行客户端工具（executed=1）
            //   → 回填 tool 结果再发 → LocalLlm 识别 role=tool 去重 → 落默认终答
            // 验证「LLM 决策 → wire 传输 → transport 执行 → 回喂终答」彻底接入（与真实后端共用同一 wire 循环）。
            var port = FindFreePort();
            var prefix = $"http://127.0.0.1:{port}/";
            var llmOptions = new LocalLlmOptions
            {
                DefaultResponse = "（本地后端·LocalLlm）任务已创建完成",
                DelayMs = 30,
                Rules = new()
                {
                    new() { Match = "regex:请调用工具", Mode = "tool_call", ToolName = "demo_tool", ToolArguments = "{\"k\":\"v\"}" },
                },
            };
            var serverLocal = new LocalLlmClient(llmOptions);
            var host = LocalLlmHttpModule.Start(
                new LocalLlmHttpOptions { Enabled = true, ListenPrefix = prefix, Model = "local-llm" }, serverLocal);
            try
            {
                var client = new OpenAiCompatibleLlmClient(prefix, apiKey: null, "local-llm", name: "local-http");
                var executed = 0;
                var tool = new LlmTool("demo_tool", "demo", _ =>
                {
                    System.Threading.Interlocked.Increment(ref executed);
                    return Task.FromResult("工具结果: 已创建");
                });
                var r = client.ChatWithToolsAsync("sys", "请调用工具", new[] { tool }).GetAwaiter().GetResult();
                Assert.IsTrue(r.IsSuccess, $"wire 工具循环应成功，Error={r.Error}");
                Assert.AreEqual(1, Volatile.Read(ref executed), "transport 应恰好执行一次工具调用（服务端回填后去重，不重复调用）");
                Assert.IsFalse(string.IsNullOrWhiteSpace(r.Content), "终答应为非空文本");
                StringAssert.Contains(r.Content, "任务已创建完成", "工具调用回喂后应落到默认终答");
            }
            finally
            {
                host.Dispose();
            }
        }

        [TestMethod]
        public void ClineLocalEndpoint_Fixed8787_FullHosting_ReturnsNonEmpty()
        {
            // 完全复现 GUI 装配：LlmHosting.Configure(真实 agent-config) + 固定 8787 LocalLlmHttpHost + LocalOnly + ClineLocalEndpoint。
            // 若此处返回非空，则 GUI textLen=0 的断点不在 LLM 装配链路，而在 cline_execute 的 seed/prompt 输入。
            var config = new Agent.Launcher.AgentConfig();
            config.ApplyExternalConfig();
            config.ApplyLlmDefaults();
            Agent.Launcher.LlmHosting.Configure(config);
            var serverLocal = LlmClientFactory.Instance.Get("local") as LocalLlmClient;
            var host = LocalLlmHttpModule.Start(config.LocalLlmHttp, serverLocal);
            var prevLocalOnly = LlmMode.LocalOnly;
            LlmMode.LocalOnly = true;
            try
            {
                var endpoint = new ClineLocalEndpoint();
                var request = new AgentExecutorRequest("system", new[] { "GUI 测试：统一 LLM 网关本地路由，请用一句话回复" });
                var result = endpoint.ExecuteAsync(NodeExecutionContract.ToNodeRequest(request)).GetAwaiter().GetResult();
                Assert.IsTrue(result.Ok, $"ClineLocalEndpoint 应 Ok，Error={result.Error}");
                Assert.IsFalse(string.IsNullOrWhiteSpace(result.Text),
                    $"GUI 装配下 ClineLocalEndpoint 应返回非空 Text，实际 Text='{result.Text}'");
            }
            finally
            {
                LlmMode.LocalOnly = prevLocalOnly;
                host.Dispose();
            }
        }

        [TestMethod]
        public void ClineLocalEndpoint_ReturnsNonEmptyText_ThroughRealChain()
        {
            // 完整模拟 GUI 的 cline_execute 本地路径：ClineLocalEndpoint → LlmGateway.Current(本地腿)
            // → LocalLlmHttpHost → LocalLlmClient。确认本地返回的非空内容能穿透到 ClineResult.Text
            // （resp 非空但 textLen=0 时，锁定是 ClineLocalEndpoint/本地腿拿不到，还是终答组装丢）。
            var port = FindFreePort();
            var prefix = $"http://127.0.0.1:{port}/";
            var llmOptions = new LocalLlmOptions
            {
                DefaultResponse = "（本地后端·LocalLlm）已处理问题：{user}",
                EchoInput = true,
                DelayMs = 30,
            };
            var serverLocal = new LocalLlmClient(llmOptions);
            var host = LocalLlmHttpModule.Start(
                new LocalLlmHttpOptions { Enabled = true, ListenPrefix = prefix, Model = "local-llm" }, serverLocal);
            var localLeg = new OpenAiCompatibleLlmClient(prefix, apiKey: null, "local-llm", name: "local-http");
            var routing = new LlmRoutingClient(localLeg, () => serverLocal, () => true);

            var prevRouting = LlmMode.Routing;
            var prevLocalOnly = LlmMode.LocalOnly;
            LlmMode.Configure(routing);
            LlmMode.LocalOnly = true;
            try
            {
                var endpoint = new ClineLocalEndpoint();
                var request = new AgentExecutorRequest("system", new[] { "GUI 测试：统一 LLM 网关本地路由，请用一句话回复" });
                var result = endpoint.ExecuteAsync(NodeExecutionContract.ToNodeRequest(request)).GetAwaiter().GetResult();
                Assert.IsTrue(result.Ok, $"ClineLocalEndpoint 应 Ok，实际 Error={result.Error}");
                Assert.IsFalse(string.IsNullOrWhiteSpace(result.Text),
                    $"ClineLocalEndpoint 应返回非空 Text（resp 非空但 textLen=0 的定位关键），实际 Text='{result.Text}'");
            }
            finally
            {
                LlmMode.Configure(prevRouting);
                LlmMode.LocalOnly = prevLocalOnly;
                host.Dispose();
            }
        }
    }
}
