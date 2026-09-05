using System.Text.Json;

namespace CallCenter.Mcp;

/// <summary>MCP 端点侧工具元数据（语言无关，供投影为 CallCenter 数据面工具描述符）。</summary>
public sealed record McpToolInfo(
    string Name,
    string? Description,
    JsonElement? InputSchema);

/// <summary>一次 MCP 工具调用结果（统一返回，不绑定 SDK 具体类型）。</summary>
public sealed record McpCallResponse(
    bool IsError,
    string? Text)
{
    public static McpCallResponse Ok(string text) => new(false, text);

    public static McpCallResponse Fail(string text) => new(true, text);
}

/// <summary>
/// 到 MCP 端点的工具调用端口：由 <see cref="McpClientConnection"/> 用官方 ModelContextProtocol SDK 承载
/// stdio 连接，供单测以内存实现注入（不必拉起 node 进程）即可驱动 <see cref="McpBridgeTrack"/>。
/// </summary>
public interface IMcpToolClient : IAsyncDisposable
{
    Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(bool forceRefresh = false, CancellationToken ct = default);

    Task<McpCallResponse> CallToolAsync(string toolName, string? argumentsJson, CancellationToken ct = default);
}
