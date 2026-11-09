using CallCenter.LlmGate.Ledger;
using CallCenter.LlmGate.Ports;

namespace CallCenter.LlmGate.Ports;

/// <summary>
/// 端口装配单点：同进程 Hosting 写 <see cref="Bind"/>，函数库只读。
/// 未绑定 ⇒ 回退跨进程 HTTP 客户端（默认 127.0.0.1:8790，可用 LLM_GATE_URL 覆盖）。
/// </summary>
public static class LlmGatePorts
{
    public const string UrlEnv = "LLM_GATE_URL";
    public const string DefaultUrl = "http://127.0.0.1:8790";

    private static readonly object Sync = new();
    private static ILlmGateControl? _control;
    private static ILlmGateQuery? _query;
    private static LlmGateHttpPorts? _remote;

    public static bool IsBound => Volatile.Read(ref _control) is not null;

    public static void Bind(ILlmGateControl control, ILlmGateQuery query)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(query);
        lock (Sync)
        {
            _control = control;
            _query = query;
            _remote = null;
        }
    }

    public static void Unbind()
    {
        lock (Sync)
        {
            _control = null;
            _query = null;
            _remote = null;
        }
    }

    /// <summary>
    /// 取端口：已绑定优先；否则回退远端 HTTP。远端不探测连通性（懒连接），
    /// 由调用方在首次请求时收到连接异常并转成「未装配」文案。
    /// </summary>
    public static LlmGatePortSet ResolveOrRemote(string? url = null)
    {
        var control = Volatile.Read(ref _control);
        var query = Volatile.Read(ref _query);
        if (control is not null && query is not null)
        {
            return new LlmGatePortSet(control, query, null);
        }

        lock (Sync)
        {
            if (_control is not null && _query is not null)
            {
                return new LlmGatePortSet(_control, _query, null);
            }

            var target = string.IsNullOrWhiteSpace(url)
                ? Environment.GetEnvironmentVariable(UrlEnv) is { Length: > 0 } env ? env : DefaultUrl
                : url;

            _remote ??= new LlmGateHttpPorts(target);
            return new LlmGatePortSet(_remote, _remote, null);
        }
    }

    public static string RemoteUrl => _remote?.BaseUrl
        ?? (Environment.GetEnvironmentVariable(UrlEnv) is { Length: > 0 } env ? env : DefaultUrl);
}

/// <summary>端口组（Control/Query 同源；Message 为装配失败说明）。</summary>
public sealed record LlmGatePortSet(ILlmGateControl? Control, ILlmGateQuery? Query, string? Message)
{
    public bool IsAvailable => Control is not null && Query is not null;
}
