using System.Text.Json;

namespace Operation.StateBlock;
/// <summary>发送给 daemon 的 JSON-Line 请求（通用帧协议，与具体工具无关）。</summary>
public sealed record DaemonRequest(
    string Id,
    string Cmd,
    object? Payload = null)
{
    public static DaemonRequest Ping(string id) => new(id, "ping");
}

/// <summary>daemon 返回的 JSON-Line 帧（完成响应 / 心跳 / pong / csg 统一模型）。</summary>
public sealed record DaemonResponse(
    string? Id,
    bool Ok,
    string? Type,
    JsonElement? Result,
    string? Error,
    long? ElapsedMs,
    string? Stage = null,
    JsonElement? Detail = null,
    string? Form = null)
{
    /// <summary>
    /// 是否为终结响应（带 ok 标志：成功或失败）。
    /// 协议 schema 规定终结帧 type 为「缺省或 result」；两者均视为终结，避免显式 type:"result" 的终结帧被误判为非终结而挂起等待。
    /// </summary>
    public bool IsTerminal => (Type is null || string.Equals(Type, "result", StringComparison.Ordinal)) && (Ok || Error is not null);
}
