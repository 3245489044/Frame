using System.Text.Json;
using CallCenter.Core.Tools;
using ModelContextProtocol.Client;

namespace CallCenter.Mcp;

/// <summary>
/// 到 CallCenter 本机 HTTP MCP server 承载的 Loopback client（Streamable HTTP）。用官方 SDK
/// <see cref="HttpClientTransport"/> 连 <c>http://localhost:{port}/mcp</c>，与外部 Cline/Claude 同一端点。
/// 实现 <see cref="IMcpToolClient"/>（stdio <see cref="McpClientConnection"/> 的 HTTP 姊妹，SingleImplementation/TH-12）。
/// Agent/FileDriven 经此回连 CallCenter 承载调用工具，不再自持 HTTP server。调用方持有生命周期（DisposeAsync）。
/// </summary>
public sealed class CallCenterHttpMcpClient : IMcpToolClient
{
    private const int RequestTimeoutSeconds = 60;

    /// <summary>缺省本机承载端口（Host --http 未指定 --port 时由承载预留空闲端口，client 缺省连本端口；测试可覆盖）。</summary>
    public const int DefaultMcpPort = 3201;

    private readonly Uri _endpoint;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private McpClient? _client;
    private bool _connected;
    private bool _disposed;

    private CallCenterHttpMcpClient(Uri endpoint) => _endpoint = endpoint;

    /// <summary>是否已建立 MCP 会话。</summary>
    public bool IsConnected => _connected;

    /// <summary>以指定端点连接（endpoint 缺省 localhost:DefaultMcpPort/mcp）。</summary>
    public static async Task<CallCenterHttpMcpClient> ConnectAsync(
        Uri? endpoint = null, int? port = null, string path = "/mcp", CancellationToken ct = default)
    {
        var uri = endpoint ?? new Uri($"http://localhost:{port ?? DefaultMcpPort}{path}");
        var client = new CallCenterHttpMcpClient(uri);
        await client.EnsureConnectedAsync(ct).ConfigureAwait(false);
        return client;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        var mcp = await EnsureConnectedAsync(ct).ConfigureAwait(false);
        var tools = new List<McpToolInfo>();
        foreach (var t in await mcp.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false))
            tools.Add(new McpToolInfo(t.Name, t.Description, default));
        return tools;
    }

    /// <inheritdoc />
    public async Task<McpCallResponse> CallToolAsync(string toolName, string? argumentsJson, CancellationToken ct = default)
    {
        var mcp = await EnsureConnectedAsync(ct).ConfigureAwait(false);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(RequestTimeoutSeconds));

        var response = await mcp.CallToolAsync(toolName, McpProtocol.ParseArguments(argumentsJson), cancellationToken: cts.Token).ConfigureAwait(false);
        var text = McpProtocol.ExtractText(response.Content);
        return response.IsError == true
            ? McpCallResponse.Fail(string.IsNullOrWhiteSpace(text) ? $"调用 '{toolName}' 失败（MCP isError）" : text)
            : McpCallResponse.Ok(string.IsNullOrWhiteSpace(text) ? "null" : text);
    }

    private async Task<McpClient> EnsureConnectedAsync(CancellationToken ct)
    {
        if (_connected && _client is not null)
            return _client;

        await _connectGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_connected && _client is not null)
                return _client;

            ThrowIfDisposed();
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = _endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
            });
            _client = await McpClient.CreateAsync(transport, new McpClientOptions(), cancellationToken: ct).ConfigureAwait(false);
            _connected = true;
            return _client;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    /// <summary>
    /// 与具体端点 / 传输无关的入参解析与 content 提取已收敛至 <see cref="McpProtocol"/>（SingleImplementation / TH-12）。
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CallCenterHttpMcpClient));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _connected = false;

        var c = _client;
        _client = null;
        if (c is not null)
        {
            try
            {
                await c.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // 释放会话尽力而为，不抛。
            }
        }

        _connectGate.Dispose();
    }
}
