using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Agent.Contract.Llm;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Agent.Axioms.Tests
{
    /// <summary>
    /// 统一 OpenAI 兼容 client（<see cref="OpenAiCompatibleLlmClient"/>）测试 —— 本地回环验证「一条通道」：
    /// 用自包含的 OpenAI 兼容 HTTP 存根（与 LocalLlmHttp 同协议，<c>POST /v1/chat/completions</c>）做真实 HTTP 往返，
    /// 覆盖 chat / chat-messages / json（response_format）/ 错误 / 解析 / Function Calling 工具循环 / BaseUrl 归一化。
    /// 核心论点：本地与真实后端共用同一 client，仅 BaseUrl 不同（本地打 8787，真实打 DeepSeek）。
    /// </summary>
    [TestClass]
    public class OpenAiCompatibleLlmClientTests
    {
        /// <summary>自包含 OpenAI 兼容 HTTP 存根（HttpListener），按请求返回响应体/状态，可捕获最近请求体供断言。</summary>
        private sealed class StubOpenAiServer : IDisposable, IAsyncDisposable
        {
            private readonly HttpListener _listener = new();
            private readonly string _prefix;
            private readonly Func<string, string> _responseBodyFor;
            private readonly Func<string, HttpStatusCode>? _statusFor;
            private readonly object _lock = new();
            private Task? _loop;
            private int _started;
            private int _requestCount;

            public string LastRequestBody = "";
            public string LastHopHeader = "";

            public StubOpenAiServer(Func<string, string> responseBodyFor, Func<string, HttpStatusCode>? statusFor = null)
            {
                var port = FindFreePort();
                _prefix = $"http://127.0.0.1:{port}/";
                _responseBodyFor = responseBodyFor;
                _statusFor = statusFor;
                Port = port;
            }

            public int Port { get; }

            public int RequestCount
            {
                get { lock (_lock) return _requestCount; }
            }

            private static int FindFreePort()
            {
                var l = new TcpListener(IPAddress.Loopback, 0);
                l.Start();
                var p = ((IPEndPoint)l.LocalEndpoint).Port;
                l.Stop();
                return p;
            }

            public void Start()
            {
                if (Interlocked.Exchange(ref _started, 1) == 1) return;
                _listener.Prefixes.Add(_prefix);
                _listener.Start();
                _loop = Task.Run(async () =>
                {
                    while (Volatile.Read(ref _started) != 0)
                    {
                        HttpListenerContext? ctx;
                        try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
                        catch { break; }
                        _ = HandleAsync(ctx);
                    }
                });
            }

            private async Task HandleAsync(HttpListenerContext ctx)
            {
                lock (_lock) _requestCount++;
                try
                {
                    LastHopHeader = ctx.Request.Headers[LlmRecursionGuard.HopHeader] ?? "";
                    var body = await new System.IO.StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync().ConfigureAwait(false);
                    LastRequestBody = body;
                    var status = _statusFor?.Invoke(body) ?? HttpStatusCode.OK;
                    var respBody = _responseBodyFor(body);
                    var bytes = Encoding.UTF8.GetBytes(respBody);
                    ctx.Response.StatusCode = (int)status;
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.ContentLength64 = bytes.Length;
                    await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                    ctx.Response.Close();
                }
                catch { try { ctx.Response.Abort(); } catch { } }
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _started, 0);
                try { _listener.Stop(); } catch { }
                try { _listener.Close(); } catch { }
            }

            public async ValueTask DisposeAsync()
            {
                Interlocked.Exchange(ref _started, 0);
                try { _listener.Stop(); } catch { }
                try { if (_loop is not null) await _loop.ConfigureAwait(false); } catch { }
                try { _listener.Close(); } catch { }
            }
        }

        private static string ChatBody(string content) => JsonSerializer.Serialize(new
        {
            choices = new[] { new { index = 0, message = new { role = "assistant", content }, finish_reason = "stop" } },
        });

        private static string ToolCallsBody(string id, string name, string args) => JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { index = 0, message = new { role = "assistant", content = (string?)null,
                    tool_calls = new[] { new { id, type = "function", function = new { name, arguments = args } } } }, finish_reason = "tool_calls" },
            },
        });

        private static string BaseUrl(int port) => $"http://127.0.0.1:{port}";

        [TestMethod]
        public void ChatAsync_Loopback_ReturnsContent()
        {
            using var server = new StubOpenAiServer(_ => ChatBody("hello"));
            server.Start();
            var client = new OpenAiCompatibleLlmClient(BaseUrl(server.Port), "test-key", "test-model", name: "stub");

            var r = client.ChatAsync("sys", "user").GetAwaiter().GetResult();

            Assert.IsTrue(r.IsSuccess, "回环命中应成功");
            Assert.AreEqual("hello", r.Content);
            Assert.AreEqual(1, server.RequestCount);
        }

        [TestMethod]
        public void ChatAsync_EmptyContent_ReturnsExplicitFailure_NotSilentSuccess()
        {
            // 空结果契约（框架层）：后端返回空文本 → 显式失败（IsSuccess=false + 归因），
            // 而非静默 IsSuccess=true/Content=null。避免下游执行端拿到空内容仍判成功、产出空终答且零归因日志。
            using var server = new StubOpenAiServer(_ => ChatBody(""));
            server.Start();
            var client = new OpenAiCompatibleLlmClient(BaseUrl(server.Port), "test-key", "test-model", name: "stub");

            var r = client.ChatAsync("sys", "user").GetAwaiter().GetResult();

            Assert.IsFalse(r.IsSuccess, "空内容响应应归因失败而非静默成功");
            StringAssert.Contains(r.Error, "空响应");
            Assert.IsNull(r.Content);
        }

        [TestMethod]
        public void ChatMessagesAsync_Loopback_PreservesFullMessages()
        {
            using var server = new StubOpenAiServer(_ => ChatBody("ok"));
            server.Start();
            var client = new OpenAiCompatibleLlmClient(BaseUrl(server.Port), "test-key", "test-model");

            var r = client.ChatMessagesAsync(new[] { LlmMessage.System("s"), LlmMessage.User("u") }).GetAwaiter().GetResult();

            Assert.IsTrue(r.IsSuccess);
            Assert.AreEqual("ok", r.Content);
            using var doc = JsonDocument.Parse(server.LastRequestBody);
            Assert.AreEqual(2, doc.RootElement.GetProperty("messages").GetArrayLength());
        }

        [TestMethod]
        public void GenerateJsonAsync_SendsResponseFormatJsonObject()
        {
            using var server = new StubOpenAiServer(_ => ChatBody("{\"action\":\"continue\"}"));
            server.Start();
            var client = new OpenAiCompatibleLlmClient(BaseUrl(server.Port), "test-key", "test-model");

            var r = client.GenerateJsonAsync("sys", "user").GetAwaiter().GetResult();

            Assert.IsTrue(r.IsSuccess);
            using var doc = JsonDocument.Parse(server.LastRequestBody);
            var rf = doc.RootElement.GetProperty("response_format");
            Assert.AreEqual("json_object", rf.GetProperty("type").GetString());
        }

        [TestMethod]
        public void HttpError_ReturnsStructuredFailure()
        {
            using var server = new StubOpenAiServer(_ => "{\"error\":{\"message\":\"boom\"}}", _ => HttpStatusCode.InternalServerError);
            server.Start();
            var client = new OpenAiCompatibleLlmClient(BaseUrl(server.Port), "test-key", "test-model");

            var r = client.ChatAsync("sys", "user").GetAwaiter().GetResult();

            Assert.IsFalse(r.IsSuccess, "非 2xx 应归因失败而非抛到调用方");
            StringAssert.Contains(r.Error, "500");
        }

        [TestMethod]
        public void MalformedResponse_ReturnsFailure()
        {
            using var server = new StubOpenAiServer(_ => "not-json{{");
            server.Start();
            var client = new OpenAiCompatibleLlmClient(BaseUrl(server.Port), "test-key", "test-model");

            var r = client.ChatAsync("sys", "user").GetAwaiter().GetResult();

            Assert.IsFalse(r.IsSuccess);
            StringAssert.Contains(r.Error, "解析失败");
        }

        [TestMethod]
        public void ChatWithToolsAsync_ExecutesToolLoop_ReturnsFinalContent()
        {
            // 首次请求返回 tool_calls，其后（回填 tool 结果后）返回最终文本。
            StubOpenAiServer? srv = null;
            srv = new StubOpenAiServer(_ => srv!.RequestCount == 1 ? ToolCallsBody("call_1", "calc", "{\"x\":1}") : ChatBody("final"));
            var server = srv;
            server.Start();
            var client = new OpenAiCompatibleLlmClient(BaseUrl(server.Port), "test-key", "test-model");

            var executed = 0;
            var tool = new LlmTool("calc", "计算器", async args => { executed++; return "42"; });

            try
            {
                var r = client.ChatWithToolsAsync("sys", "user", tools: new[] { tool }).GetAwaiter().GetResult();

                Assert.IsTrue(r.IsSuccess);
                Assert.AreEqual("final", r.Content);
                Assert.AreEqual(1, executed, "工具应被执行一次");
                Assert.AreEqual(2, server.RequestCount, "应有一次工具调用 → 再发一次");
            }
            finally
            {
                server.Dispose();
            }
        }

        [TestMethod]
        public void BaseUrl_WithOrWithoutV1_BothHitSameEndpoint()
        {
            using var server = new StubOpenAiServer(_ => ChatBody("x"));
            server.Start();
            // 带 /v1（Cline 形态）与不带 /v1（DeepSeek 形态）都应归一化到 {base}/v1/chat/completions，命中同一端点。
            var withV1 = new OpenAiCompatibleLlmClient($"{BaseUrl(server.Port)}/v1", "k", "m");
            var withoutV1 = new OpenAiCompatibleLlmClient(BaseUrl(server.Port), "k", "m");

            var r1 = withV1.ChatAsync("s", "u").GetAwaiter().GetResult();
            var r2 = withoutV1.ChatAsync("s", "u").GetAwaiter().GetResult();

            Assert.IsTrue(r1.IsSuccess && r2.IsSuccess);
            Assert.AreEqual(2, server.RequestCount);
        }

        [TestMethod]
        public void ChatAsync_SendsHopHeader_ExternalEntryIsZero()
        {
            using var server = new StubOpenAiServer(_ => ChatBody("x"));
            server.Start();
            var client = new OpenAiCompatibleLlmClient(BaseUrl(server.Port), "k", "m");

            var r = client.ChatAsync("s", "u").GetAwaiter().GetResult();

            Assert.IsTrue(r.IsSuccess);
            Assert.AreEqual("0", server.LastHopHeader, "外部入口默认跳数为 0（X-Llm-Hop）");
        }

        [TestMethod]
        public void RecursionHop_ExplicitInjection_TransmittedAndBounded()
        {
            // P3-1 显式跳数传递（ILlmHopPropagator）：服务端经 ChatMessagesWithHopAsync 显式携带 hop，替代进程级 CurrentHop 静态（消除跨请求串扰）。
            using var server = new StubOpenAiServer(_ => ChatBody("x"));
            server.Start();
            var client = new OpenAiCompatibleLlmClient(BaseUrl(server.Port), "k", "m");

            // hop 显式透传到出站 X-Llm-Hop 头
            var r1 = client.ChatMessagesWithHopAsync(new[] { LlmMessage.User("u") }, hop: 1).GetAwaiter().GetResult();
            Assert.IsTrue(r1.IsSuccess);
            Assert.AreEqual("1", server.LastHopHeader, "显式 hop 应原样写入出站 X-Llm-Hop 头");

            // hop 越界判定（服务端据此 508 强制停止）
            Assert.IsFalse(LlmRecursionGuard.IsWithinLimit(LlmRecursionGuard.MaxHop),
                $"hop ≥ {LlmRecursionGuard.MaxHop} 应判定越界（服务端据此强制停止）");
        }

        [TestMethod]
        public void RecursionGuard_IsWithinLimit_Boundary()
        {
            Assert.IsTrue(LlmRecursionGuard.IsWithinLimit(0), "hop=0 合法");
            Assert.IsTrue(LlmRecursionGuard.IsWithinLimit(LlmRecursionGuard.MaxHop - 1), "hop=MaxHop-1 合法");
            Assert.IsFalse(LlmRecursionGuard.IsWithinLimit(LlmRecursionGuard.MaxHop), "hop=MaxHop 越界");
            Assert.IsFalse(LlmRecursionGuard.IsWithinLimit(-1), "hop<0 越界");
        }
    }
}
