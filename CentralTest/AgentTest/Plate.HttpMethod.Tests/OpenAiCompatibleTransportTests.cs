using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Plate.HttpMethod.OpenAI;
using Plate.HttpMethod.Transport;

namespace Plate.HttpMethod.Tests
{
    [TestClass]
    public class OpenAiCompatibleTransportTests
    {
        /// <summary>自包含 OpenAI 兼容 HTTP 存根（HttpListener），按请求返回响应体/状态，可捕获最近请求体与 hop 头。</summary>
        private sealed class StubServer : IDisposable
        {
            private readonly HttpListener _listener = new();
            private readonly string _prefix;
            private readonly Func<string, (int status, string body)> _respond;
            private readonly object _lock = new();
            private Task? _loop;
            private int _started;

            public string LastRequestBody = "";
            public string LastHopHeader = "";

            public StubServer(Func<string, (int, string)> respond)
            {
                var port = FindFreePort();
                _prefix = $"http://127.0.0.1:{port}/";
                _respond = respond;
                Port = port;
            }

            public int Port { get; }

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
                try
                {
                    lock (_lock) { }
                    LastHopHeader = ctx.Request.Headers["X-Llm-Hop"] ?? "";
                    var body = await new System.IO.StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync().ConfigureAwait(false);
                    LastRequestBody = body;
                    var (status, respBody) = _respond(body);
                    var bytes = Encoding.UTF8.GetBytes(respBody);
                    ctx.Response.StatusCode = status;
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

            private static int FindFreePort()
            {
                var l = new TcpListener(IPAddress.Loopback, 0);
                l.Start();
                var p = ((IPEndPoint)l.LocalEndpoint).Port;
                l.Stop();
                return p;
            }
        }

        private static string ChatBody(string content) => JsonSerializer.Serialize(new
        {
            choices = new[] { new { index = 0, message = new { role = "assistant", content }, finish_reason = "stop" } },
        });

        private static string ToolCallBody(string id, string name, string args) => JsonSerializer.Serialize(new
        {
            choices = new[] { new { index = 0, message = new { role = "assistant", content = (string?)null, tool_calls = new[] { new { id, type = "function", function = new { name, arguments = args } } } }, finish_reason = "tool_calls" } },
        });

        private OpenAiCompatibleTransport NewTransport(StubServer server)
            => new(HttpTransportFactory.CreateInstance(), $"http://127.0.0.1:{server.Port}/v1/chat/completions", apiKey: null, model: "m", name: "test");

        [TestMethod]
        public async Task ChatAsync_ParsesContent()
        {
            using var server = new StubServer(_ => (200, ChatBody("你好")));
            server.Start();
            var transport = NewTransport(server);

            var result = await transport.ChatAsync(new[] { new OpenAiChatMessage("user", "hi") }).ConfigureAwait(false);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("你好", result.Content);
        }

        [TestMethod]
        public async Task ChatAsync_Non2xx_ReturnsFail()
        {
            using var server = new StubServer(_ => (500, "boom"));
            server.Start();
            var transport = NewTransport(server);

            var result = await transport.ChatAsync(new[] { new OpenAiChatMessage("user", "hi") }).ConfigureAwait(false);

            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains(result.Error ?? "", "500");
        }

        [TestMethod]
        public async Task JsonMode_RequestContainsResponseFormat()
        {
            using var server = new StubServer(_ => (200, ChatBody("{\"a\":1}")));
            server.Start();
            var transport = NewTransport(server);

            await transport.ChatAsync(new[] { new OpenAiChatMessage("user", "json") }, jsonMode: true).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(server.LastRequestBody);
            Assert.IsTrue(doc.RootElement.TryGetProperty("response_format", out _), "jsonMode 请求应含 response_format");
        }

        [TestMethod]
        public async Task ChatAsync_WritesHopHeader()
        {
            using var server = new StubServer(_ => (200, ChatBody("ok")));
            server.Start();
            var transport = NewTransport(server);

            await transport.ChatAsync(new[] { new OpenAiChatMessage("user", "hi") }, hop: 3).ConfigureAwait(false);

            Assert.AreEqual("3", server.LastHopHeader, "显式 hop 应原样写入出站 X-Llm-Hop 头");
        }

        [TestMethod]
        public async Task ChatWithTools_LoopsUntilFinalText()
        {
            using var server = new StubServer(body =>
            {
                // 首次请求（1 条 user 消息）→ 返回 tool_calls；第二次（含 assistant+tool 回填）→ 返回最终文本。
                using var doc = JsonDocument.Parse(body);
                var count = doc.RootElement.GetProperty("messages").GetArrayLength();
                return count <= 1 ? (200, ToolCallBody("call_1", "get_weather", "{\"city\":\"bj\"}")) : (200, ChatBody("最终文本"));
            });
            server.Start();
            var transport = NewTransport(server);

            var tool = new OpenAiTool("get_weather", "天气", _ => Task.FromResult("晴天"));
            var result = await transport.ChatAsync(
                new[] { new OpenAiChatMessage("user", "天气如何") }, new[] { tool }).ConfigureAwait(false);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("最终文本", result.Content);
        }

    }
}
