using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plate.HttpMethod.Guard;

namespace Plate.HttpMethod.OpenAI
{
    /// <summary>OpenAI 兼容服务端宿主配置。</summary>
    public sealed class OpenAiCompatibleHostOptions
    {
        /// <summary>监听前缀（如 http://127.0.0.1:8787/）。仅回环绑定，不对外暴露（P3-2 约束）。</summary>
        public string ListenPrefix { get; set; } = "http://127.0.0.1:8787/";

        /// <summary>对外暴露的模型名（/v1/models 与响应体 model 字段）。</summary>
        public string Model { get; set; } = "local-llm";

        /// <summary>健康检查服务名（/health 响应 service 字段）。</summary>
        public string ServiceName { get; set; } = "OpenAiCompatibleHttp";

        /// <summary>单次后端调用超时（秒）：到期强制取消后端调用链并返回 504（对齐 Cline 看门狗"到期强制停止"；&lt;=0 视为 60）。默认 60。</summary>
        public int RequestTimeoutSec { get; set; } = 60;

        /// <summary>递归跳数护栏（请求头 + 服务端 508）。</summary>
        public RecursionHopGuard Guard { get; set; } = new();

        /// <summary>可选日志回调（name, data），供上层接入日志门面；null = 静默。</summary>
        public Action<string, object?>? Logger { get; set; }
    }

    /// <summary>
    /// OpenAI 兼容 HTTP 服务端宿主（通用 wire 层，不依赖任何领域契约）—— 把任意后端暴露为 OpenAI 兼容端点
    /// （Kestrel + Minimal API）：<c>POST /v1/chat/completions</c>（含 stream SSE）、<c>POST /v1/responses</c>、
    /// <c>GET /v1/models</c>、<c>GET /health(/z)</c>。
    /// <para>传输层仅做协议编解码 + 递归护栏（<see cref="RecursionHopGuard"/>）+ 单次看门狗超时，
    /// 回答逻辑唯一委托 <c>Func&lt;IReadOnlyList&lt;OpenAiChatMessage&gt;, int, CancellationToken → Task&lt;OpenAiChatResult&gt;&gt;</c>，
    /// 不复制业务逻辑（SingleImplementation）。仅回环绑定（127.0.0.1），不对外暴露。</para>
    /// </summary>
    public sealed class OpenAiCompatibleHost : IAsyncDisposable, IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
        };

        private readonly Func<IReadOnlyList<OpenAiChatMessage>, IReadOnlyList<string>, int, CancellationToken, Task<OpenAiChatResult>> _backend;
        private readonly OpenAiCompatibleHostOptions _options;
        private readonly TimeSpan _requestTimeout;
        private readonly CancellationTokenSource _cts = new();
        private WebApplication? _app;
        private int _started;

        public OpenAiCompatibleHost(
            Func<IReadOnlyList<OpenAiChatMessage>, IReadOnlyList<string>, int, CancellationToken, Task<OpenAiChatResult>> backend,
            OpenAiCompatibleHostOptions? options = null)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _options = options ?? new OpenAiCompatibleHostOptions();
            // 单次后端调用看门狗（对齐 Cline daemon：到期强制停止，不无限空等）。
            _requestTimeout = TimeSpan.FromSeconds(_options.RequestTimeoutSec > 0 ? _options.RequestTimeoutSec : 60);
        }

        /// <summary>监听前缀（如 http://127.0.0.1:8787/）。</summary>
        public string ListenPrefix => _options.ListenPrefix;

        /// <summary>是否已启动。</summary>
        public bool IsRunning => Volatile.Read(ref _started) != 0;

        /// <summary>启动 Kestrel 监听（幂等）。无 ACL urlacl 前置要求；端口占用/绑定失败抛异常。</summary>
        public void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) == 1) return;
            try
            {
                _app = BuildApp();
                _app.StartAsync(_cts.Token).GetAwaiter().GetResult();
                _options.Logger?.Invoke("OpenAiCompatibleHost_started", new { prefix = _options.ListenPrefix, model = _options.Model });
            }
            catch
            {
                Interlocked.Exchange(ref _started, 0);
                try { if (_app is not null) _app.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
                _app = null;
                throw;
            }
        }

        /// <summary>停止监听（幂等）。</summary>
        public void Stop()
        {
            if (Volatile.Read(ref _started) == 0) return;
            Interlocked.Exchange(ref _started, 0);
            _cts.Cancel();
            try { _app?.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(); } catch { /* 已停止/未启动忽略 */ }
        }

        public async ValueTask DisposeAsync()
        {
            Stop();
            try { if (_app is not null) await _app.DisposeAsync().ConfigureAwait(false); } catch { /* 取消竞态忽略 */ }
            _cts.Dispose();
        }

        /// <summary>同步释放（供 <see cref="System.IDisposable"/> 生命周期；Stop 后即停止监听）。</summary>
        public void Dispose()
        {
            Stop();
            try { if (_app is not null) _app.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
            _cts.Dispose();
        }

        // ---- 端点装配（Kestrel + Minimal API） ----

        private WebApplication BuildApp()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = Array.Empty<string>() });
            // 仅回环绑定（127.0.0.1，P3-2 约束）：服务端不对外暴露；若未来需跨进程访问，另行补充认证。
            builder.WebHost.UseUrls(_options.ListenPrefix);
            // 抑制 ASP.NET Core 默认控制台日志（Microsoft.Hosting.Lifetime 的 Now listening / Application started /
            // Request finished 等宿主生命周期噪音）；本宿主诊断走 _options.Logger 回调，不依赖 Microsoft.Extensions.Logging。
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            var app = builder.Build();
            app.MapGet("/health", HandleHealth);
            app.MapGet("/healthz", HandleHealth);
            app.MapGet("/v1/models", HandleModels);
            app.MapPost("/v1/chat/completions", HandleChatCompletions);
            app.MapPost("/v1/responses", HandleResponses);
            return app;
        }

        private IResult HandleHealth() => Json(new { status = "ok", service = _options.ServiceName });

        private IResult HandleModels() => Json(new
        {
            @object = "list",
            data = new[] { new { id = _options.Model, @object = "model", owned_by = "local-llm" } },
        });

        // ---- 协议处理器 ----

        private async Task<IResult> HandleChatCompletions(HttpRequest req, HttpResponse res, CancellationToken ct)
        {
            // 递归跳数守卫：读入跳数头（外部默认 0），超上限即 508 强制停止（阻断 HTTP 自环的纵深防御）。
            var hop = ReadHop(req);
            if (!_options.Guard.IsWithinLimit(hop))
            {
                _options.Logger?.Invoke("OpenAiCompatibleHost_recursion_guard_triggered",
                    new { hop, maxHop = _options.Guard.MaxHop, path = "/v1/chat/completions" });
                return JsonError(508, "loop_detected",
                    $"检测到递归调用（hop={hop} ≥ {_options.Guard.MaxHop}），强制停止以阻断无限调用。");
            }

            var json = await ReadBodyAsync(req, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var model = GetString(root, "model") ?? _options.Model;
            var stream = root.TryGetProperty("stream", out var s) && s.ValueKind == JsonValueKind.True;

            var messages = ParseChatMessages(root);
            var toolNames = ParseToolNames(root);
            // 定制消息直通：完整 messages 数组（含任意 role / 多轮 / tool 消息）交后端按消息数组渲染；
            // tools[].function.name 透传工具名（wire 层只透传名字+回写调用，不执行——执行在客户端 transport，对齐真实后端语义）。
            // 经 RunBackendAsync 施加单次看门狗超时；hop 经后端委托显式出站（防 HTTP 自环纵深防御）。
            var result = await RunBackendAsync(t => _backend(messages, toolNames, hop, t), ct).ConfigureAwait(false);
            if (result is null)
                return JsonError(504, "watchdog_timeout",
                    $"单次后端调用超时（>{_requestTimeout.TotalSeconds:0}s），已强制停止。");
            if (!result.IsSuccess)
                return JsonError(502, "upstream_error", result.Error ?? "后端失败");

            var text = result.Content ?? "";
            var id = "chatcmpl-" + Guid.NewGuid().ToString("N");
            var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var hasTools = result.ToolCalls is { Count: > 0 };
            // 工具调用以 OpenAI 标准 message.tool_calls 回写（客户端 transport 解析并执行）；客户端工具循环固定 stream=false，
            // 工具调用结果不支持流式表达，显式报错而非静默丢工具。
            if (stream && hasTools)
                return JsonError(400, "tool_calls_not_streamable", "工具调用结果不支持流式返回（请用 stream=false）。");
            if (stream)
                return Sse(res, id, model, created, text);

            object message = hasTools
                ? (object)new
                {
                    role = "assistant",
                    content = (string?)null,
                    tool_calls = SerializeToolCalls(result.ToolCalls!),
                }
                : (object)new { role = "assistant", content = text };

            return Json(new
            {
                id,
                @object = "chat.completion",
                created,
                model,
                choices = new[]
                {
                    new { index = 0, message, finish_reason = hasTools ? (string?)null : "stop" },
                },
                usage = new { prompt_tokens = 0, completion_tokens = 0, total_tokens = 0 },
            });
        }

        /// <summary>Responses 协议最小非流式子集：instructions 作 system，input 消息数组作 history/user。</summary>
        private async Task<IResult> HandleResponses(HttpRequest req, CancellationToken ct)
        {
            var hop = ReadHop(req);
            if (!_options.Guard.IsWithinLimit(hop))
            {
                _options.Logger?.Invoke("OpenAiCompatibleHost_recursion_guard_triggered",
                    new { hop, maxHop = _options.Guard.MaxHop, path = "/v1/responses" });
                return JsonError(508, "loop_detected",
                    $"检测到递归调用（hop={hop} ≥ {_options.Guard.MaxHop}），强制停止以阻断无限调用。");
            }

            var json = await ReadBodyAsync(req, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var model = GetString(root, "model") ?? _options.Model;

            // 定制消息直通：instructions 作 system 前缀 + input 消息数组（任意 role），完整结构交后端按消息数组渲染。
            // Responses 协议最小子集暂不支持 tools（透传空工具名，保持无工具路径语义）。
            var result = await RunBackendAsync(t => _backend(ParseResponsesMessages(root), Array.Empty<string>(), hop, t), ct).ConfigureAwait(false);
            if (result is null)
                return JsonError(504, "watchdog_timeout",
                    $"单次后端调用超时（>{_requestTimeout.TotalSeconds:0}s），已强制停止。");
            if (!result.IsSuccess)
                return JsonError(502, "upstream_error", result.Error ?? "后端失败");

            var text = result.Content ?? "";
            return Json(new
            {
                id = "resp-" + Guid.NewGuid().ToString("N"),
                @object = "response",
                created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                status = "completed",
                model,
                output = new[]
                {
                    new
                    {
                        type = "message",
                        id = "msg-" + Guid.NewGuid().ToString("N"),
                        status = "completed",
                        role = "assistant",
                        content = new[] { new { type = "output_text", text, annotations = Array.Empty<object>() } },
                    },
                },
                usage = new { input_tokens = 0, output_tokens = 0, total_tokens = 0 },
            });
        }

        /// <summary>
        /// 执行一次后端调用：施加**单次看门狗超时**（对齐 Cline daemon：预算到期强制停止，不再无限空等）。
        /// 超时（非服务关闭）→ 返回 null，由调用方回 504；后端失败 → 返回 <see cref="OpenAiChatResult.Fail"/>。
        /// </summary>
        private async Task<OpenAiChatResult?> RunBackendAsync(
            Func<CancellationToken, Task<OpenAiChatResult>> invoke, CancellationToken ct)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_requestTimeout);
            try
            {
                return await invoke(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // 单次后端调用看门狗到期（非服务关闭）→ 强制停止并回超时失败。
                _options.Logger?.Invoke("OpenAiCompatibleHost_watchdog_timeout", new { timeoutSec = _requestTimeout.TotalSeconds });
                return null;
            }
        }

        // ---- 消息解析（OpenAI wire → OpenAiChatMessage） ----

        /// <summary>解析 chat completions 的 messages 数组（任意 role / 多轮 / 字符串或多段 content）为 wire 消息列表。</summary>
        private static List<OpenAiChatMessage> ParseChatMessages(JsonElement root)
        {
            var list = new List<OpenAiChatMessage>();
            if (root.TryGetProperty("messages", out var ms) && ms.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in ms.EnumerateArray())
                {
                    if (m.ValueKind != JsonValueKind.Object) continue;
                    var role = GetString(m, "role");
                    if (string.IsNullOrWhiteSpace(role)) continue;
                    var content = ExtractContent(m, "content");
                    // P2：保留 assistant(tool_calls)（content 可能为空）与 tool(tool_call_id) 回填消息，
                    // 使 L1 多轮工具循环在 LocalLlmHttp 与真实后端间语义一致（否则回填被过滤掉）。
                    var toolCalls = ParseToolCalls(m);
                    var toolCallId = GetString(m, "tool_call_id");
                    if (string.IsNullOrWhiteSpace(content) && toolCalls is null && string.IsNullOrEmpty(toolCallId)) continue;
                    list.Add(new OpenAiChatMessage(role, content)
                    {
                        ToolCalls = toolCalls,
                        ToolCallId = toolCallId,
                    });
                }
            }
            return list;
        }

        /// <summary>解析 message.tool_calls（OpenAI 标准格式）为 wire 工具调用列表；缺失/空 → null。</summary>
        private static List<OpenAiToolCall>? ParseToolCalls(JsonElement m)
        {
            if (!m.TryGetProperty("tool_calls", out var tc) || tc.ValueKind != JsonValueKind.Array) return null;
            var calls = new List<OpenAiToolCall>();
            foreach (var call in tc.EnumerateArray())
            {
                if (call.ValueKind != JsonValueKind.Object) continue;
                var id = GetString(call, "id") ?? "";
                var name = "";
                var args = "";
                if (call.TryGetProperty("function", out var fn) && fn.ValueKind == JsonValueKind.Object)
                {
                    name = GetString(fn, "name") ?? "";
                    args = GetString(fn, "arguments") ?? "";
                }
                calls.Add(new OpenAiToolCall(id, name, args));
            }
            return calls.Count > 0 ? calls : null;
        }

        /// <summary>解析 Responses 输入为完整消息数组：instructions 作 system 前缀，input 消息项（任意 role / 字符串）追加。</summary>
        private static List<OpenAiChatMessage> ParseResponsesMessages(JsonElement root)
        {
            var list = new List<OpenAiChatMessage>();
            var instructions = GetString(root, "instructions");
            if (!string.IsNullOrWhiteSpace(instructions)) list.Add(new OpenAiChatMessage("system", instructions));

            if (root.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in input.EnumerateArray())
                {
                    string? role;
                    string content;
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        role = "user";
                        content = item.GetString() ?? "";
                    }
                    else if (item.ValueKind == JsonValueKind.Object)
                    {
                        role = GetString(item, "role") ?? "user";
                        content = ExtractContent(item, "content");
                    }
                    else
                    {
                        continue;
                    }

                    // P2：同 ParseChatMessages —— 保留携带工具字段的空文本消息（多轮回填）。
                    var itemToolCalls = ParseToolCalls(item);
                    var itemToolCallId = GetString(item, "tool_call_id");
                    if (string.IsNullOrWhiteSpace(content) && itemToolCalls is null && string.IsNullOrEmpty(itemToolCallId)) continue;
                    list.Add(new OpenAiChatMessage(role, content)
                    {
                        ToolCalls = itemToolCalls,
                        ToolCallId = itemToolCallId,
                    });
                }
            }

            if (list.Count == 0) list.Add(new OpenAiChatMessage("user", ""));
            return list;
        }

        /// <summary>提取消息 content（字符串，或字符串/对象数组中的 text / input_text）。</summary>
        private static string ExtractContent(JsonElement m, string prop)
        {
            if (m.ValueKind == JsonValueKind.String) return m.GetString() ?? "";
            if (m.ValueKind != JsonValueKind.Object || !m.TryGetProperty(prop, out var c)) return "";
            if (c.ValueKind == JsonValueKind.String) return c.GetString() ?? "";
            if (c.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var part in c.EnumerateArray())
                {
                    if (part.ValueKind == JsonValueKind.String) { sb.Append(part.GetString()); continue; }
                    if (part.ValueKind != JsonValueKind.Object) continue;
                    if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String) sb.Append(t.GetString());
                    else if (part.TryGetProperty("input_text", out var it) && it.ValueKind == JsonValueKind.String) sb.Append(it.GetString());
                }
                return sb.ToString().Trim();
            }
            return "";
        }

        private static string? GetString(JsonElement o, string name)
            => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        /// <summary>解析请求 body 的 <c>tools[].function.name</c> 为工具名列表（Function Calling 工具名透传；wire 层只透传名字，不执行）。</summary>
        private static List<string> ParseToolNames(JsonElement root)
        {
            var names = new List<string>();
            if (root.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tools.EnumerateArray())
                {
                    if (t.ValueKind != JsonValueKind.Object) continue;
                    if (t.TryGetProperty("function", out var fn) && fn.ValueKind == JsonValueKind.Object)
                    {
                        var n = GetString(fn, "name");
                        if (!string.IsNullOrWhiteSpace(n)) names.Add(n);
                    }
                }
            }
            return names;
        }

        /// <summary>把后端返回的工具调用序列化为 <c>chat.completion.message.tool_calls</c>（OpenAI 标准格式，供客户端 transport 解析执行）。</summary>
        private static List<object> SerializeToolCalls(IReadOnlyList<OpenAiToolCall> calls)
        {
            var list = new List<object>(calls.Count);
            foreach (var c in calls)
                list.Add(new
                {
                    id = c.Id ?? "",
                    type = "function",
                    function = new { name = c.Name, arguments = c.Arguments ?? "" },
                });
            return list;
        }

        // ---- 传输辅助 ----

        /// <summary>读入递归跳数头（缺失/非法默认 0，即外部入口）。</summary>
        private int ReadHop(HttpRequest req)
        {
            var h = req.Headers[_options.Guard.HeaderName].ToString();
            return int.TryParse(h, out var v) ? v : 0;
        }

        private static async Task<string> ReadBodyAsync(HttpRequest req, CancellationToken ct)
        {
            using var reader = new StreamReader(req.Body, Encoding.UTF8);
            return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }

        /// <summary>SSE 流式响应（Kestrel 原生流式）：单 chunk + [DONE]，与迁移前 HTTP 冒烟契约一致。</summary>
        private static IResult Sse(HttpResponse res, string id, string model, long created, string text)
        {
            res.Headers["Cache-Control"] = "no-cache";
            var chunk = new
            {
                id,
                @object = "chat.completion.chunk",
                created,
                model,
                choices = new[]
                {
                    new { index = 0, delta = new { role = "assistant", content = text }, finish_reason = (string?)null },
                },
            };
            var line = "data: " + JsonSerializer.Serialize(chunk, JsonOptions) + "\ndata: [DONE]\n\n";
            return Results.Stream(new System.IO.MemoryStream(Encoding.UTF8.GetBytes(line)), "text/event-stream");
        }

        private static IResult Json(object body) => Results.Json(body, JsonOptions);

        private static IResult JsonError(int status, string type, string message)
            => Results.Json(new { error = new { type, message } }, JsonOptions, statusCode: status);



    }
}
