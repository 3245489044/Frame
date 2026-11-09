using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using CallCenter.LlmGate.Ledger;
using CallCenter.LlmGate.Model;

namespace CallCenter.LlmGate.Ports;

/// <summary>
/// 跨进程端口实现（BCL HttpClient）：窗口函数库在独立代理进程之外时使用。
/// 只调 /api/gate/*，不承担 HTTP 宿主职责。
/// </summary>
public sealed class LlmGateHttpPorts : ILlmGateControl, ILlmGateQuery, IDisposable
{
    private readonly HttpClient _http;

    public LlmGateHttpPorts(string baseUrl, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("LlmGate URL 不能为空", nameof(baseUrl));
        }

        BaseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl + "/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(5),
        };
    }

    public string BaseUrl { get; }

    // ---- 控制面 ----

    public string Status() => GetText("api/gate/status");

    public string SetMode(string mode)
        => PostText("api/gate/mode", new { mode });

    public string Halt(string? reason)
        => PostText("api/gate/halt", new { reason });

    public string Resume() => PostText("api/gate/resume", new { });

    public bool TryApprove(string callId, out string message)
    {
        message = PostText("api/gate/approve", new { callId });
        return message.StartsWith("已放行", StringComparison.Ordinal);
    }

    public bool TryDeny(string callId, string? reason, out string message)
    {
        message = PostText("api/gate/deny", new { callId, reason });
        return message.StartsWith("已拒绝", StringComparison.Ordinal);
    }

    // ---- 读面 ----

    public GateStats SnapshotStats()
        => GetJson<GateStats>("api/gate/stats")
           ?? throw new InvalidOperationException("LlmGate 统计响应为空");

    public IReadOnlyList<LlmCallSpan> SnapshotCalls(string? filter, int take)
    {
        var url = $"api/gate/calls?take={(take <= 0 ? 200 : take)}";
        if (!string.IsNullOrWhiteSpace(filter))
        {
            url += "&filter=" + Uri.EscapeDataString(filter);
        }

        return GetJson<List<LlmCallSpan>>(url) ?? new List<LlmCallSpan>();
    }

    public IReadOnlyList<PendingView> SnapshotPending()
        => GetJson<List<PendingView>>("api/gate/pending") ?? new List<PendingView>();

    public LlmCallSpan? Find(string callId)
    {
        if (string.IsNullOrWhiteSpace(callId))
        {
            return null;
        }

        try
        {
            return GetJson<LlmCallSpan>("api/gate/detail?callId=" + Uri.EscapeDataString(callId));
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();

    // ---- 传输 ----

    private string GetText(string relative)
    {
        using var response = _http.GetAsync(relative).ConfigureAwait(false).GetAwaiter().GetResult();
        var body = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        EnsureSuccess(response, body);
        return body;
    }

    private string PostText(string relative, object payload)
    {
        var json = JsonSerializer.Serialize(payload, GateJson.Options);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = _http.PostAsync(relative, content).ConfigureAwait(false).GetAwaiter().GetResult();
        var body = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        EnsureSuccess(response, body);
        return body;
    }

    private T? GetJson<T>(string relative)
    {
        var text = GetText(relative);
        return string.IsNullOrWhiteSpace(text)
            ? default
            : JsonSerializer.Deserialize<T>(text, GateJson.Options);
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new HttpRequestException(
            $"LlmGate 控制端点返回 {(int)response.StatusCode}: {body}",
            inner: null,
            statusCode: response.StatusCode);
    }
}
