using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HttpMethod = System.Net.Http.HttpMethod;
using Plate.HttpMethod.Guard;

namespace Plate.HttpMethod.OpenAI
{
    /// <summary>
    /// OpenAI 兼容出站传输（通用 wire 层，不依赖任何领域契约）—— 对任意 OpenAI 兼容端点
    /// <c>POST {baseUrl}/v1/chat/completions</c>。承载文本 / 消息数组 / Function Calling（tools）/ JSON（response_format=json_object）四通道。
    /// <para>本地与真实后端共用同一实现，仅 endpoint 不同；失败（网络异常 / 非 2xx / 响应解析失败 / 空文本）
    /// → <see cref="OpenAiChatResult.Fail"/>（含 status/body 摘要），不静默空等。工具多轮循环不在本层（归 L1 会话内核）。</para>
    /// </summary>
    public sealed class OpenAiCompatibleTransport
    {
        /// <summary>响应体错误摘要最大字符数。</summary>
        public const int ErrorSummaryMax = 500;

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly HttpClient _http;
        private readonly string _endpoint;   // {baseUrl}/v1/chat/completions（已归一化，不双 /v1）
        private readonly string? _apiKey;
        private readonly string _model;
        private readonly string _name;
        private readonly RecursionHopGuard _guard;

        public OpenAiCompatibleTransport(
            HttpClient http,
            string endpoint,
            string? apiKey,
            string model,
            string? name = null,
            RecursionHopGuard? guard = null)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _apiKey = apiKey;
            _model = string.IsNullOrWhiteSpace(model) ? "deepseek-chat" : model.Trim();
            _name = string.IsNullOrWhiteSpace(name) ? "openai-compatible" : name.Trim();
            _guard = guard ?? new RecursionHopGuard();
        }

        /// <summary>客户端名（provider key，用于日志归因；缺省 openai-compatible）。</summary>
        public string Name => _name;

        /// <summary>兼容入口（等价 <see cref="ChatOnceAsync"/>）：传输零工具执行，只做单次往返并透出 <c>tool_calls</c>；
        /// 工具多轮循环归 L1 会话内核（P2 执行归属收口）。新代码请直接用 <see cref="ChatOnceAsync"/>。</summary>
        public Task<OpenAiChatResult> ChatAsync(
            IReadOnlyList<OpenAiChatMessage> messages,
            IReadOnlyList<OpenAiTool>? tools = null,
            bool jsonMode = false,
            double? temperature = null,
            int hop = 0,
            CancellationToken ct = default)
            => ChatOnceAsync(messages, tools, jsonMode, temperature, hop, ct);

        /// <summary>单次往返（传输零领域：只发一次并透出 <c>tool_calls</c>，<b>不执行、不回填</b>）。
        /// 多轮工具循环归 L1 会话内核（P2 执行归属收口）：内核执行工具后携带 assistant/tool 消息再调本入口。
        /// 空响应契约沿用：无文本且无 tool_calls → <see cref="OpenAiChatResult.Fail"/>。</summary>
        public async Task<OpenAiChatResult> ChatOnceAsync(
            IReadOnlyList<OpenAiChatMessage> messages,
            IReadOnlyList<OpenAiTool>? tools = null,
            bool jsonMode = false,
            double? temperature = null,
            int hop = 0,
            CancellationToken ct = default)
        {
            var parsed = await SendOnceAsync(
                messages?.ToList() ?? new List<OpenAiChatMessage>(),
                temperature, jsonMode,
                (tools ?? Array.Empty<OpenAiTool>()).ToList(),
                hop, ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(parsed.Error))
                return OpenAiChatResult.Fail(parsed.Error);
            if (string.IsNullOrWhiteSpace(parsed.Content) && (parsed.ToolCalls is null || parsed.ToolCalls.Count == 0))
                return OpenAiChatResult.Fail($"{_name} 返回空响应（无文本内容，可能后端失败或未生成文本）");
            return OpenAiChatResult.Ok(parsed.Content, parsed.ToolCalls);
        }

        /// <summary>执行一次 HTTP POST 并解析响应（网络/状态/解析错误 → ParsedResponse.Error）。</summary>
        private async Task<ParsedResponse> SendOnceAsync(
            List<OpenAiChatMessage> messages, double? temperature, bool jsonMode,
            List<OpenAiTool> tools, int hop, CancellationToken ct)
        {
            var body = new Dictionary<string, object?>
            {
                ["model"] = _model,
                ["messages"] = messages.Select(SerializeMessage).ToList(),
                ["stream"] = false,
            };
            if (temperature is not null) body["temperature"] = temperature.Value;
            if (jsonMode) body["response_format"] = new { type = "json_object" };
            if (tools.Count > 0) body["tools"] = SerializeTools(tools);

            string responseBody;
            try
            {
                var json = JsonSerializer.Serialize(body, JsonOptions);
                using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, _endpoint)
                { Content = new StringContent(json, Encoding.UTF8, "application/json") };
                // 递归跳数头（显式）：hop 由调用方显式传入（外部入口 0；服务端经 hop 传播显式传 hop+1），
                // 服务端据此检测并硬限自环深度（见 RecursionHopGuard）。真实端点忽略未知头。
                req.Headers.TryAddWithoutValidation(_guard.HeaderName, _guard.BuildHeaderValue(hop));
                if (!string.IsNullOrWhiteSpace(_apiKey))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                responseBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return new ParsedResponse(null, null,
                        $"OpenAI 兼容端点返回 {(int)resp.StatusCode}（{_name}）: {Truncate(responseBody)}");
            }
            catch (Exception ex)
            {
                return new ParsedResponse(null, null, $"OpenAI 兼容端点请求失败（{_name}）: {ex.Message}");
            }

            return ParseResponse(responseBody, _name);
        }

        // ---- 响应解析 ----

        private static ParsedResponse ParseResponse(string responseBody, string name)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;
                if (!root.TryGetProperty("choices", out var choices)
                    || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                    return new ParsedResponse(null, null, $"OpenAI 兼容端点响应缺 choices（{name}）: {Truncate(responseBody)}");

                var msg = choices[0].GetProperty("message");
                string? content = msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                    ? c.GetString()
                    : null;

                if (msg.TryGetProperty("tool_calls", out var tc)
                    && tc.ValueKind == JsonValueKind.Array && tc.GetArrayLength() > 0)
                {
                    var calls = new List<OpenAiToolCall>();
                    foreach (var call in tc.EnumerateArray())
                    {
                        var id = call.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                            ? idEl.GetString() ?? "" : "";
                        string fnName = "", fnArgs = "";
                        if (call.TryGetProperty("function", out var fn))
                        {
                            if (fn.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) fnName = n.GetString() ?? "";
                            if (fn.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.String) fnArgs = a.GetString() ?? "";
                        }
                        calls.Add(new OpenAiToolCall(id, fnName, fnArgs));
                    }
                    return new ParsedResponse(content, calls, null);
                }

                return new ParsedResponse(content, null, null);
            }
            catch (Exception ex)
            {
                return new ParsedResponse(null, null, $"OpenAI 兼容端点响应解析失败（{name}）: {ex.Message}");
            }
        }

        // ---- 消息/工具序列化 ----

        private static object SerializeMessage(OpenAiChatMessage m)
        {
            var dict = new Dictionary<string, object?>
            {
                ["role"] = m.Role,
                ["content"] = m.Content,
            };
            if (m.ToolCalls is { Count: > 0 })
                dict["tool_calls"] = m.ToolCalls.Select(c => (object?)new
                {
                    id = c.Id,
                    type = "function",
                    function = new { name = c.Name, arguments = c.Arguments },
                }).ToList();
            if (!string.IsNullOrEmpty(m.ToolCallId))
                dict["tool_call_id"] = m.ToolCallId;
            return dict;
        }

        private static List<object> SerializeTools(List<OpenAiTool> tools)
        {
            var list = new List<object>();
            foreach (var t in tools)
            {
                var parameters = t.InputSchema is { ValueKind: not JsonValueKind.Undefined } schema
                    ? (object)schema
                    : (object)new { type = "object", properties = new { } };
                list.Add(new
                {
                    type = "function",
                    function = new { name = t.Name, description = t.Description ?? "", parameters },
                });
            }
            return list;
        }

        // ---- 辅助 ----

        /// <summary>归一化 BaseUrl → <c>{baseUrl}/v1/chat/completions</c>：去结尾 '/'、去已含的 '/v1'（防双 /v1）。</summary>
        public static string BuildEndpoint(string? baseUrl, string defaultBaseUrl)
        {
            var u = (baseUrl ?? "").Trim();
            if (string.IsNullOrEmpty(u)) u = defaultBaseUrl;
            u = u.TrimEnd('/');
            if (u.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                u = u.Substring(0, u.Length - 3).TrimEnd('/');
            return u + "/v1/chat/completions";
        }

        private static string Truncate(string s, int max = ErrorSummaryMax)
            => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max) + "…");

        /// <summary>单次响应解析结果。</summary>
        private sealed record ParsedResponse(string? Content, List<OpenAiToolCall>? ToolCalls, string? Error);



    }
}
