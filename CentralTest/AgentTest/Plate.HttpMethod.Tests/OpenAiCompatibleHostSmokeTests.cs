using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using HttpMethod = System.Net.Http.HttpMethod;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Plate.HttpMethod.OpenAI;

namespace Plate.HttpMethod.Tests
{
    [TestClass]
    public class OpenAiCompatibleHostSmokeTests
    {
        private static async Task<(OpenAiCompatibleHost host, HttpClient client, string baseUrl)> StartAsync(int port)
        {
            var host = new OpenAiCompatibleHost(
                (messages, toolNames, hop, ct) => Task.FromResult(OpenAiChatResult.Ok("hello")),
                new OpenAiCompatibleHostOptions
                {
                    ListenPrefix = $"http://127.0.0.1:{port}/",
                    Model = "m",
                    ServiceName = "TestHost",
                    RequestTimeoutSec = 30,
                });
            host.Start();
            var client = new HttpClient();
            return (host, client, $"http://127.0.0.1:{port}");
        }

        private static int FindFreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        [TestMethod]
        public async Task Health_ReturnsOk()
        {
            var port = FindFreePort();
            var (host, client, baseUrl) = await StartAsync(port).ConfigureAwait(false);
            try
            {
                var resp = await client.GetAsync($"{baseUrl}/health").ConfigureAwait(false);
                Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                StringAssert.Contains(json, "\"ok\"");
            }
            finally
            {
                client.Dispose();
                await host.DisposeAsync().ConfigureAwait(false);
            }
        }

        [TestMethod]
        public async Task Models_ReturnsList()
        {
            var port = FindFreePort();
            var (host, client, baseUrl) = await StartAsync(port).ConfigureAwait(false);
            try
            {
                var resp = await client.GetAsync($"{baseUrl}/v1/models").ConfigureAwait(false);
                Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                StringAssert.Contains(json, "\"m\"");
            }
            finally
            {
                client.Dispose();
                await host.DisposeAsync().ConfigureAwait(false);
            }
        }

        [TestMethod]
        public async Task ChatCompletions_NonStream_ReturnsText()
        {
            var port = FindFreePort();
            var (host, client, baseUrl) = await StartAsync(port).ConfigureAwait(false);
            try
            {
                var payload = "{\"model\":\"m\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}],\"stream\":false}";
                using var req = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"{baseUrl}/v1/chat/completions")
                { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
                var resp = await client.SendAsync(req).ConfigureAwait(false);
                Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                StringAssert.Contains(json, "\"hello\"");
            }
            finally
            {
                client.Dispose();
                await host.DisposeAsync().ConfigureAwait(false);
            }
        }

        [TestMethod]
        public async Task ChatCompletions_Stream_ReturnsSse()
        {
            var port = FindFreePort();
            var (host, client, baseUrl) = await StartAsync(port).ConfigureAwait(false);
            try
            {
                var payload = "{\"model\":\"m\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}],\"stream\":true}";
                using var req = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"{baseUrl}/v1/chat/completions")
                { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
                var resp = await client.SendAsync(req).ConfigureAwait(false);
                Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
                Assert.AreEqual("text/event-stream", resp.Content.Headers.ContentType?.MediaType, "SSE 冒烟契约：content-type=text/event-stream");
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                StringAssert.Contains(body, "data: [DONE]", "SSE 冒烟契约：应以 data: [DONE] 收尾");
            }
            finally
            {
                client.Dispose();
                await host.DisposeAsync().ConfigureAwait(false);
            }
        }

        [TestMethod]
        public async Task ChatCompletions_WithTools_ReturnsToolCalls()
        {
            var port = FindFreePort();
            var prefix = $"http://127.0.0.1:{port}/";
            var host = new OpenAiCompatibleHost(
                (messages, toolNames, hop, ct) =>
                    toolNames.Contains("create_node_reference")
                        ? Task.FromResult(OpenAiChatResult.Ok(null, new[]
                        {
                            new OpenAiToolCall("call_1", "create_node_reference", "{\"TaskKey\":\"x\"}"),
                        }))
                        : Task.FromResult(OpenAiChatResult.Ok("no-tools")),
                new OpenAiCompatibleHostOptions
                {
                    ListenPrefix = prefix,
                    Model = "m",
                    ServiceName = "TestHost",
                    RequestTimeoutSec = 30,
                });
            host.Start();
            var client = new HttpClient();
            try
            {
                // wire 层只透传工具名给后端；后端命中同名工具时回写 tool_calls，客户端 transport 据此执行（本冒烟验证服务端侧序列化）。
                var payload = "{\"model\":\"m\",\"messages\":[{\"role\":\"user\",\"content\":\"创建任务\"}],"
                    + "\"tools\":[{\"type\":\"function\",\"function\":{\"name\":\"create_node_reference\"}}],\"stream\":false}";
                using var req = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"{prefix}v1/chat/completions")
                { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
                var resp = await client.SendAsync(req).ConfigureAwait(false);
                Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                StringAssert.Contains(json, "tool_calls", "服务端应回写 message.tool_calls");
                StringAssert.Contains(json, "create_node_reference", "tool_calls 应含工具名");
                StringAssert.Contains(json, "call_1", "tool_calls 应含 id");
            }
            finally
            {
                client.Dispose();
                await host.DisposeAsync().ConfigureAwait(false);
            }
        }

        [TestMethod]
        public async Task Responses_ReturnsOutputText()
        {
            var port = FindFreePort();
            var (host, client, baseUrl) = await StartAsync(port).ConfigureAwait(false);
            try
            {
                var payload = "{\"model\":\"m\",\"input\":\"hi\"}";
                using var req = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"{baseUrl}/v1/responses")
                { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
                var resp = await client.SendAsync(req).ConfigureAwait(false);
                Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                StringAssert.Contains(json, "\"hello\"");
            }
            finally
            {
                client.Dispose();
                await host.DisposeAsync().ConfigureAwait(false);
            }
        }

    }
}
