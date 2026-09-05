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
    /// <para>本地与真实后端共用同一实现，仅 endpoint 不同；失败（网络异常 / 非 2xx / 响应解析失败 / 工具循环超限 / 空文本）
    /// → <see cref="OpenAiChatResult.Fail"/>（含 status/body 摘要），不静默空等。</para>
    /// </summary>
    public sealed class OpenAiCompatibleTransport
    {
        /// <summary>Function Calling 工具调用循环默认最大迭代数（防 LLM 反复调工具死循环）。</summary>
        public const int DefaultMaxToolIterations = 8;

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

        /// <summary>无工具循环入口：统一委派到带工具循环实现（无 tools 时首次迭代即返回）。</summary>
        public Task<OpenAiChatResult> ChatAsync(
            IReadOnlyList<OpenAiChatMessage> messages,
            IReadOnlyList<OpenAiTool>? tools = null,
            bool jsonMode = false,
            double? temperature = null,
            int hop = 0,
            int maxToolIterations = DefaultMaxToolIterations,
            CancellationToken ct = default)
            => ChatWithToolsAsync(messages, tools ?? Array.Empty<OpenAiTool>(), jsonMode, temperature, hop, maxToolIterations, ct);

        /// <summary>带 Function Calling 工具循环的调用：发请求 → 有 tool_calls 则执行工具并回填 → 再发，直至无 tool_calls 或超限。</summary>
        private async Task<OpenAiChatResult> ChatWithToolsAsync(
            IReadOnlyList<OpenAiChatMessage> messages,
            IReadOnlyList<OpenAiTool> tools,
            bool jsonMode, double? temperature, int hop, int maxToolIterations, CancellationToken ct)
        {
            var list = messages.ToList();
            var toolList = tools.ToList();
            for (var i = 0; i < maxToolIterations; i++)
            {
                var parsed = await SendOnceAsync(list, temperature, jsonMode, toolList, hop, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(parsed.Error))
                    return OpenAiChatResult.Fail(parsed.Error);
                if (parsed.ToolCalls is null || parsed.ToolCalls.Count == 0 || toolList.Count == 0)
                {
                    // 空结果契约（模型管理面·框架层）：后端返回无文本内容 → 显式失败而非静默
                    // IsSuccess=true/Content=null。避免下游执行端拿到空内容仍判成功、产出空终答且零归因日志。
                    if (string.IsNullOrWhiteSpace(parsed.Content))
                        return OpenAiChatResult.Fail($"{_name} 返回空响应（无文本内容，可能后端失败或未生成文本）");
                    return OpenAiChatResult.Ok(parsed.Content); // 无工具调用 / 后端不支持工具 → 最终文本
                }

                // 有 tool_calls → 回填 assistant（含 tool_calls）+ 逐个执行工具回填 tool 消息
                list.Add(new OpenAiChatMessage
                {
                    Role = "assistant",
                    Content = parsed.Content,
                    ToolCalls = parsed.ToolCalls,
                });
                foreach (var call in parsed.ToolCalls)
                {
                    var toolResult = await ExecuteToolAsync(toolList, call).ConfigureAwait(false);
                    list.Add(new OpenAiChatMessage { Role = "tool", ToolCallId = call.Id, Content = toolResult });
                }
            }
            return OpenAiChatResult.Fail($"OpenAI 兼容端点在 {maxToolIterations} 次工具调用循环内未产出最终文本（{_name}）。");
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

        private static async Task<string> ExecuteToolAsync(List<OpenAiTool> toolList, OpenAiToolCall call)
        {
            var tool = toolList.FirstOrDefault(t => t.Name == call.Name);
            if (tool is null) return $"工具不存在: {call.Name}";
            try
            {
                return await tool.ExecuteAsync(call.Arguments).ConfigureAwait(false) ?? "";
            }
            catch (Exception ex)
            {
                return $"工具执行失败: {ex.Message}";
            }
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
