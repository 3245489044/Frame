using System.Net.Http;
using System.Text;
using System.Text.Json;
using CallCenter.Cost.Costing;
using CallCenter.Cost.Pricing;
using CallCenter.Cost.Usage;
using CallCenter.LlmGate.Control;
using CallCenter.LlmGate.Costing;
using CallCenter.LlmGate.Ledger;
using CallCenter.LlmGate.Model;
using CallCenter.LlmGate.Ports;
using CallCenter.LlmGate.Rules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plate.HttpMethod.Guard;
using Plate.HttpMethod.Transport;

namespace CallCenter.LlmGate.Hosting;

/// <summary>
/// LlmGate 代理宿主：Kestrel + 控制/读模型 API + OpenAI 兼容 wire 透传（含 SSE）。
/// 判据只读结构事实（指纹/计数/token/金额/hop），不读执行者叙述。
/// </summary>
public sealed class LlmGateProxyHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly GateCore _core;
    private readonly HttpClient _http;
    private readonly IPriceTable _prices;
    private readonly RecursionHopGuard _hopGuard;
    private readonly Action<string>? _log;
    private readonly TimeProvider _time = TimeProvider.System;

    private LlmGateProxyHost(
        WebApplication app, GateCore core, IPriceTable prices, Action<string>? log)
    {
        _app = app;
        _core = core;
        _prices = prices;
        _hopGuard = new RecursionHopGuard(core.Options.MaxHop);
        _http = HttpTransportFactory.Create();
        _log = log;
    }

    public GateCore Core => _core;
    public string ListenUrl => _core.Listen;
    public string Upstream => _core.Upstream;

    public static async Task<LlmGateProxyHost> StartAsync(
        GateOptions options, string? pricingPath, Action<string>? log, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var prices = GatePricingLoader.Load(pricingPath ?? GateDataPaths.DefaultPricingPath(), log);
        var core = new GateCore(options, prices);

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = "CallCenter.LlmGate",
            Args = Array.Empty<string>(),
        });
        builder.WebHost.UseUrls(options.Listen);
        builder.Logging.ClearProviders();

        var app = builder.Build();
        var host = new LlmGateProxyHost(app, core, prices, log);
        host.Map(app);

        await app.StartAsync(ct).ConfigureAwait(false);
        LlmGatePorts.Bind(core, core);
        log?.Invoke($"[llm-gate] listen={options.Listen} upstream={options.Upstream} mode={options.Mode}");
        return host;
    }

    public Task WaitForShutdownAsync(CancellationToken ct) => _app.WaitForShutdownAsync(ct);

    public async ValueTask DisposeAsync()
    {
        _core.Pending.Clear();
        try
        {
            await _app.StopAsync().ConfigureAwait(false);
        }
        catch
        {
            // 停止阶段异常不掩盖主流程；进程退出即回收。
        }

        await _app.DisposeAsync().ConfigureAwait(false);
    }
    // ================= 路由 =================

    private void Map(WebApplication app)
    {
        app.MapGet("/api/gate/status", (HttpContext _) =>
            Results.Text(_core.Status(), "text/plain; charset=utf-8"));

        app.MapGet("/api/gate/stats", (HttpContext _) =>
            Results.Json(_core.SnapshotStats(), GateJson.Options));

        app.MapGet("/api/gate/calls", (HttpContext ctx) =>
        {
            var filter = ctx.Request.Query["filter"].FirstOrDefault();
            var take = int.TryParse(ctx.Request.Query["take"].FirstOrDefault(), out var n) ? n : 200;
            return Results.Json(_core.SnapshotCalls(filter, take), GateJson.Options);
        });

        app.MapGet("/api/gate/pending", (HttpContext _) =>
            Results.Json(_core.SnapshotPending(), GateJson.Options));

        app.MapGet("/api/gate/detail", (HttpContext ctx) =>
        {
            var callId = ctx.Request.Query["callId"].FirstOrDefault();
            var span = _core.Find(callId ?? string.Empty);
            return span is null ? Results.NotFound() : Results.Json(span, GateJson.Options);
        });

        app.MapPost("/api/gate/approve", async (HttpContext ctx) =>
        {
            var request = await ReadJsonAsync<CallIdRequest>(ctx).ConfigureAwait(false);
            _core.TryApprove(request?.CallId ?? string.Empty, out var message);
            return Results.Text(message, "text/plain; charset=utf-8");
        });

        app.MapPost("/api/gate/deny", async (HttpContext ctx) =>
        {
            var request = await ReadJsonAsync<DenyRequest>(ctx).ConfigureAwait(false);
            _core.TryDeny(request?.CallId ?? string.Empty, request?.Reason, out var message);
            return Results.Text(message, "text/plain; charset=utf-8");
        });

        app.MapPost("/api/gate/halt", async (HttpContext ctx) =>
        {
            var request = await ReadJsonAsync<HaltRequest>(ctx).ConfigureAwait(false);
            return Results.Text(_core.Halt(request?.Reason), "text/plain; charset=utf-8");
        });

        app.MapPost("/api/gate/resume", (HttpContext _) =>
            Results.Text(_core.Resume(), "text/plain; charset=utf-8"));

        app.MapPost("/api/gate/mode", async (HttpContext ctx) =>
        {
            var request = await ReadJsonAsync<ModeRequest>(ctx).ConfigureAwait(false);
            return Results.Text(_core.SetMode(request?.Mode ?? string.Empty), "text/plain; charset=utf-8");
        });

        // OpenAI 兼容 wire（含任意路径）：低优先级 catch-all。
        app.Map("/{**path}", (HttpContext ctx) => HandleProxyAsync(ctx));
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpContext ctx)
    {
        try
        {
            return await ctx.Request.ReadFromJsonAsync<T>().ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public sealed record CallIdRequest(string? CallId);
    public sealed record DenyRequest(string? CallId, string? Reason);
    public sealed record HaltRequest(string? Reason);
    public sealed record ModeRequest(string? Mode);
    // ================= 代理主流程 =================

    private async Task HandleProxyAsync(HttpContext ctx)
    {
        var started = _time.GetUtcNow();
        var callId = _core.NextCallId();
        var capture = await CaptureAsync(ctx, callId, ctx.RequestAborted).ConfigureAwait(false);

        var rule = _core.Evaluate(capture);
        if (rule.IsDeny)
        {
            await WriteDeniedAsync(ctx, capture, rule, DecidedBy.Rule, started, ctx.RequestAborted).ConfigureAwait(false);
            return;
        }

        var decidedBy = string.Equals(rule.RuleId, RuleIds.Bypass, StringComparison.Ordinal)
            ? DecidedBy.Bypass
            : DecidedBy.Rule;
        if (rule.IsHold)
        {
            var resolution = await _core.HoldAsync(capture, rule, ctx.RequestAborted).ConfigureAwait(false);
            if (!resolution.IsAllow)
            {
                var denied = GateRuleResult.Deny(
                    RuleIds.Manual,
                    resolution.ReasonCode ?? ReasonCodes.DenyManual,
                    resolution.Reason ?? "人工拒绝");
                await WriteDeniedAsync(ctx, capture, denied, resolution.DecidedBy, started, ctx.RequestAborted)
                    .ConfigureAwait(false);
                return;
            }

            decidedBy = resolution.DecidedBy;
        }

        await ForwardAsync(ctx, capture, decidedBy, started, ctx.RequestAborted).ConfigureAwait(false);
    }

    private async Task WriteDeniedAsync(
        HttpContext ctx, CallCapture capture, GateRuleResult rule, DecidedBy decidedBy,
        DateTimeOffset started, CancellationToken ct)
    {
        var status = rule.HttpStatus is >= 400 ? rule.HttpStatus : 403;
        var span = _core.BuildDeniedSpan(capture, rule, decidedBy, _time.GetUtcNow());
        await _core.RecordAsync(span, ct).ConfigureAwait(false);

        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await ctx.Response.WriteAsJsonAsync(new
        {
            error = new
            {
                type = "llm_gate_denied",
                code = rule.ReasonCode,
                message = rule.Message,
                call_id = capture.CallId,
                retry_after_ms = (int?)null,
            },
        }, GateJson.Options, ct).ConfigureAwait(false);
    }

    private async Task<CallCapture> CaptureAsync(HttpContext ctx, string callId, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await ctx.Request.Body.CopyToAsync(buffer, ct).ConfigureAwait(false);
        var bytes = buffer.ToArray();
        var body = Encoding.UTF8.GetString(bytes);
        var facts = OpenAiWireFacts.ParseRequest(body);

        var provider = ProviderForKey();
        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        var priceKnown = _core.Costing.TryGetQuote(provider, facts.Model, today, out _);
        var estimated = _core.Costing.EstimateInputCost(provider, facts.Model, facts.TotalChars);

        return new CallCapture(
            CallId: callId,
            Endpoint: ctx.Request.Path.Value ?? "/",
            Method: ctx.Request.Method,
            Model: facts.Model,
            Stream: facts.Stream,
            Hop: ReadHop(ctx),
            ClientId: ReadClientId(ctx),
            SessionId: ReadHeader(ctx, "X-Llm-Session"),
            TaskKey: ReadHeader(ctx, "X-Llm-Task"),
            RoundId: ReadHeader(ctx, "X-Llm-Round"),
            Caller: ReadHeader(ctx, "X-Llm-Caller"),
            MessageCount: facts.MessageCount,
            RoleHistogram: facts.RoleHistogram,
            SystemChars: facts.SystemChars,
            UserChars: facts.UserChars,
            HistoryChars: facts.HistoryChars,
            ToolCount: facts.ToolCount,
            ToolNamesHash: facts.ToolNamesHash,
            RequestBytes: bytes.Length,
            RequestFingerprint: CallCenter.LlmGate.Fingerprint.RequestFingerprint.Compute(body),
            RequestBody: body,
            TotalChars: facts.TotalChars,
            EstimatedCostUsd: estimated,
            PriceKnown: priceKnown);
    }

    private int ReadHop(HttpContext ctx)
        => int.TryParse(ReadHeader(ctx, _hopGuard.HeaderName), out var hop) && hop >= 0 ? hop : 0;

    private static string ReadClientId(HttpContext ctx)
    {
        var caller = ReadHeader(ctx, "X-Llm-Caller");
        if (!string.IsNullOrWhiteSpace(caller))
        {
            return caller!;
        }

        var userAgent = ctx.Request.Headers["User-Agent"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return "unknown";
        }

        var trimmed = userAgent!.Trim();
        var space = trimmed.IndexOf(' ');
        if (space > 0)
        {
            trimmed = trimmed[..space];
        }

        return trimmed.Length <= 60 ? trimmed : trimmed[..60];
    }

    private static string? ReadHeader(HttpContext ctx, string name)
    {
        var value = ctx.Request.Headers[name].FirstOrDefault();
        return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }

    private string ProviderForKey()
    {
        var host = Uri.TryCreate(_core.Upstream, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;
        return host.Contains("deepseek", StringComparison.OrdinalIgnoreCase) ? "DeepSeek" : "OpenAI";
    }
    // ================= 转发 =================

    private async Task ForwardAsync(
        HttpContext ctx, CallCapture capture, DecidedBy decidedBy, DateTimeOffset started, CancellationToken ct)
    {
        var baseUri = new Uri(_core.Upstream.TrimEnd('/'));
        var target = new Uri(baseUri, ctx.Request.Path.Value + ctx.Request.QueryString.Value);

        using var request = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), target);
        if (!string.IsNullOrEmpty(capture.RequestBody))
        {
            request.Content = new StringContent(capture.RequestBody, Encoding.UTF8, "application/json");
        }

        CopyRequestHeaders(ctx, request);
        request.Headers.Remove(_hopGuard.HeaderName);
        request.Headers.TryAddWithoutValidation(_hopGuard.HeaderName, _hopGuard.BuildHeaderValue(capture.Hop + 1));

        HttpResponseMessage? response = null;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (_core.Options.UpstreamTimeoutSec > 0)
        {
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_core.Options.UpstreamTimeoutSec));
        }

        var sendToken = timeoutCts.Token;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, sendToken).ConfigureAwait(false);
            await CopyResponseAsync(ctx, response, capture, decidedBy, started, sendToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await WriteUpstreamFailureAsync(ctx, capture, ex, started, ct).ConfigureAwait(false);
        }
        finally
        {
            response?.Dispose();
        }
    }

    private async Task CopyResponseAsync(
        HttpContext ctx, HttpResponseMessage response, CallCapture capture,
        DecidedBy decidedBy, DateTimeOffset started, CancellationToken ct)
    {
        ctx.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers)
        {
            if (!IsHopByHop(header.Key))
            {
                ctx.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        foreach (var header in response.Content.Headers)
        {
            if (!IsHopByHop(header.Key) && !string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        var isSse = capture.Stream || mediaType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase);

        OpenAiResponseFacts facts;
        string? responseBody = null;
        if (isSse)
        {
            var sse = new SseFacts();
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await SseTee.PumpAsync(stream, ctx.Response.Body, sse.Feed, ct).ConfigureAwait(false);
            facts = sse.ToFacts();
        }
        else
        {
            var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            await ctx.Response.Body.WriteAsync(bytes, ct).ConfigureAwait(false);
            responseBody = Encoding.UTF8.GetString(bytes);
            facts = OpenAiWireFacts.ParseResponse(responseBody);
        }

        await RecordResponseSpanAsync(ctx, capture, response, facts, responseBody, decidedBy, started, ct)
            .ConfigureAwait(false);
    }

    private static void CopyRequestHeaders(HttpContext ctx, HttpRequestMessage request)
    {
        foreach (var header in ctx.Request.Headers)
        {
            if (IsHopByHop(header.Key)
                || string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase)
                || string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase)
                || string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
    }

    private static bool IsHopByHop(string name)
        => name.Equals("Connection", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Proxy-Authenticate", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
           || name.Equals("TE", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Trailer", StringComparison.OrdinalIgnoreCase);
    private async Task RecordResponseSpanAsync(
        HttpContext ctx, CallCapture capture, HttpResponseMessage response, OpenAiResponseFacts facts,
        string? responseBody, DecidedBy decidedBy, DateTimeOffset started, CancellationToken ct)
    {
        var provider = ProviderForKey();
        var model = capture.Model ?? facts.Model;
        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        PriceQuote? quote = _core.Costing.TryGetQuote(provider, model, today, out var found) ? found : null;

        var usage = LlmUsage.UnknownUsage;
        if (facts.Usage is not null && _core.Costing.TryNormalizeUsage(provider, facts.Usage, out var normalized))
        {
            usage = normalized;
        }

        var cost = _core.Costing.Price(provider, model, usage);
        var ended = _time.GetUtcNow();
        var status = (int)response.StatusCode;

        string? errorType = null;
        string? errorCode = null;
        if (status >= 400 && !string.IsNullOrWhiteSpace(responseBody))
        {
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                {
                    errorType = OpenAiJson.ReadString(error, "type");
                    errorCode = OpenAiJson.ReadString(error, "code");
                }
            }
            catch (JsonException)
            {
                // 错误体非 JSON：保留状态码即可，不伪造分类。
            }
        }

        var span = new LlmCallSpan
        {
            CallId = capture.CallId,
            TraceId = capture.CallId.Split(':')[0],
            StartedAt = started,
            EndedAt = ended,
            DurationMs = (long)Math.Max(0, (ended - started).TotalMilliseconds),
            Endpoint = capture.Endpoint,
            UpstreamBaseUrl = _core.Upstream,
            Provider = provider,
            ModelRequest = capture.Model,
            ModelResponse = facts.Model,
            Stream = capture.Stream,
            Hop = capture.Hop,
            ClientId = capture.ClientId,
            SessionId = capture.SessionId,
            TaskKey = capture.TaskKey,
            RoundId = capture.RoundId,
            MessageCount = capture.MessageCount,
            RoleHistogram = capture.RoleHistogram,
            SystemChars = capture.SystemChars,
            UserChars = capture.UserChars,
            HistoryChars = capture.HistoryChars,
            ToolCount = capture.ToolCount,
            ToolNamesHash = capture.ToolNamesHash,
            RequestBytes = capture.RequestBytes,
            RequestFingerprint = capture.RequestFingerprint,
            StatusCode = status,
            FinishReason = facts.FinishReason,
            ContentChars = facts.ContentChars,
            ToolCallCount = facts.ToolCallCount,
            ErrorType = errorType,
            ErrorCode = errorCode,
            TokenIn = usage.PromptTotal,
            TokenOut = usage.Output,
            CacheRead = usage.CacheRead,
            CacheWrite = usage.CacheWrite,
            PriceQuote = quote,
            CostAmount = cost.Amount,
            CostCurrency = cost.Currency,
            CostBreakdown = cost.Breakdown,
            CostSource = cost.Source,
            Decision = GateDecision.Allow,
            DecidedBy = decidedBy,
            DecidedAt = ended,
            RequestBodySampled = _core.Options.StoreBody,
            RequestPreview = _core.Options.StoreBody ? Truncate(capture.RequestBody, _core.Options.PreviewChars) : null,
            ResponsePreview = _core.Options.StoreBody ? Truncate(responseBody, _core.Options.PreviewChars) : null,
        };

        await _core.RecordAsync(span, ct).ConfigureAwait(false);
    }
    private async Task WriteUpstreamFailureAsync(
        HttpContext ctx, CallCapture capture, Exception ex, DateTimeOffset started, CancellationToken ct)
    {
        var isTimeout = ex is TaskCanceledException or TimeoutException;
        var code = isTimeout ? ReasonCodes.UpstreamTimeout : ReasonCodes.UpstreamError;
        var status = isTimeout ? 504 : 502;
        var ended = _time.GetUtcNow();
        _log?.Invoke($"[llm-gate] 上游失败 callId={capture.CallId} code={code}: {ex.Message}");

        var span = new LlmCallSpan
        {
            CallId = capture.CallId,
            TraceId = capture.CallId.Split(':')[0],
            StartedAt = started,
            EndedAt = ended,
            DurationMs = (long)Math.Max(0, (ended - started).TotalMilliseconds),
            Endpoint = capture.Endpoint,
            UpstreamBaseUrl = _core.Upstream,
            ModelRequest = capture.Model,
            Stream = capture.Stream,
            Hop = capture.Hop,
            ClientId = capture.ClientId,
            SessionId = capture.SessionId,
            TaskKey = capture.TaskKey,
            RoundId = capture.RoundId,
            MessageCount = capture.MessageCount,
            RoleHistogram = capture.RoleHistogram,
            SystemChars = capture.SystemChars,
            UserChars = capture.UserChars,
            HistoryChars = capture.HistoryChars,
            ToolCount = capture.ToolCount,
            ToolNamesHash = capture.ToolNamesHash,
            RequestBytes = capture.RequestBytes,
            RequestFingerprint = capture.RequestFingerprint,
            StatusCode = status,
            ErrorType = "llm_gate_upstream",
            ErrorCode = code,
            CostAmount = null,
            CostCurrency = "USD",
            CostSource = CostSource.Unknown,
            Decision = GateDecision.Allow,
            DecidedBy = DecidedBy.Rule,
            DecidedAt = ended,
        };
        await _core.RecordAsync(span, ct).ConfigureAwait(false);

        if (ctx.Response.HasStarted)
        {
            return;
        }

        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await ctx.Response.WriteAsJsonAsync(new
        {
            error = new
            {
                type = "llm_gate_upstream",
                code,
                message = ex.Message,
                call_id = capture.CallId,
                retry_after_ms = (int?)null,
            },
        }, GateJson.Options, ct).ConfigureAwait(false);
    }

    private static string? Truncate(string? text, int chars)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var limit = Math.Max(16, chars);
        return text!.Length <= limit ? text : text[..limit];
    }

    /// <summary>SSE 末帧 usage / finish / 内容字符的旁路累计。</summary>
    private sealed class SseFacts
    {
        private IReadOnlyDictionary<string, long?>? _usage;
        private string? _model;
        private string? _finish;
        private int _contentChars;
        private int _toolCalls;

        public void Feed(string payload)
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;

                var usage = OpenAiJson.ReadUsage(root);
                if (usage is not null)
                {
                    _usage = usage;
                }

                var model = OpenAiJson.ReadString(root, "model");
                if (!string.IsNullOrWhiteSpace(model))
                {
                    _model = model;
                }

                if (!root.TryGetProperty("choices", out var choices)
                    || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                {
                    return;
                }

                var choice = choices[0];
                var finish = OpenAiJson.ReadString(choice, "finish_reason");
                if (!string.IsNullOrWhiteSpace(finish))
                {
                    _finish = finish;
                }

                if (choice.TryGetProperty("delta", out var delta))
                {
                    _contentChars += OpenAiJson.ReadContentText(delta).Length;
                    if (delta.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
                    {
                        _toolCalls = Math.Max(_toolCalls, toolCalls.GetArrayLength());
                    }
                }
            }
            catch (JsonException)
            {
                // 非 JSON 的 data 行（保活/注释）直接忽略。
            }
        }

        public OpenAiResponseFacts ToFacts() => new(_model, _finish, _contentChars, _toolCalls, _usage);
    }

}
