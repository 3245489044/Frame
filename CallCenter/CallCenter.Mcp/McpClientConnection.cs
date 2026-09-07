using ModelContextProtocol.Client;

namespace CallCenter.Mcp;

/// <summary>
/// 到 MCPBridge（Node stdio MCP server）的自包含客户端：用官方 ModelContextProtocol SDK 的 <see cref="McpClient"/>
/// 承载「spawn node + stdio + JSON-RPC」，技术路径与 Agent 侧 <see cref="McpBridgeClient"/> 同源（SingleImplementation，
/// 不复刻自研 JSON-RPC 读循环）。本实现不引用 Agent 工程，使 CallCenter 独立接入 Node MCPBridge（TS/PS 轨）。
/// 调用方持有生命周期（连接后经 <see cref="DisposeAsync"/> 释放 node 进程）。
/// </summary>
public sealed class McpClientConnection : IMcpToolClient
{
    private const string DefaultNodePath = "node";
    private const int RequestTimeoutSeconds = 60;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly string _nodePath;
    private readonly string _bridgeScriptPath;
    private readonly string _workingDirectory;

    private McpClient? _client;
    private bool _disposed;
    private bool _connected;
    private McpToolInfo[]? _cachedTools;
    private DateTime _cacheTimestamp;

    /// <summary>是否已建立 MCP 会话。</summary>
    public bool IsConnected => _connected;

    private McpClientConnection(string nodePath, string bridgeScriptPath, string workingDirectory)
    {
        _nodePath = nodePath;
        _bridgeScriptPath = bridgeScriptPath;
        _workingDirectory = workingDirectory;
    }

    /// <summary>
    /// 建立连接：以 <c>node &lt;bridgeScript&gt;</c> 起 stdio MCP 会话。入口脚本（MCPBridge build/index.js）不存在则抛
    /// <see cref="FileNotFoundException"/>（提示先构建 MCPBridge）。调用方负责在结束时 <see cref="DisposeAsync"/>。
    /// </summary>
    public static async Task<McpClientConnection> ConnectAsync(
        string bridgeScriptPath,
        string? nodePath = null,
        CancellationToken ct = default)
    {
        var full = Path.GetFullPath(bridgeScriptPath);
        if (!File.Exists(full))
            throw new FileNotFoundException(
                $"MCPBridge 入口脚本未找到: {full}。请先构建 MCPBridge 项目（node build 产出 build/index.js）。", full);

        var connection = new McpClientConnection(
            nodePath ?? DefaultNodePath,
            full,
            Path.GetDirectoryName(full) ?? Path.GetFullPath("."));
        await connection.ConnectCoreAsync(ct).ConfigureAwait(false);
        return connection;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        EnsureConnected();
        if (!forceRefresh && _cachedTools != null && DateTime.UtcNow - _cacheTimestamp < CacheTtl)
            return _cachedTools;

        var tools = new List<McpToolInfo>();
        foreach (var t in await _client!.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false))
            tools.Add(new McpToolInfo(t.Name, t.Description, default));

        _cachedTools = tools.ToArray();
        _cacheTimestamp = DateTime.UtcNow;
        return _cachedTools;
    }

    /// <inheritdoc />
    public async Task<McpCallResponse> CallToolAsync(string toolName, string? argumentsJson, CancellationToken ct = default)
    {
        EnsureConnected();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(RequestTimeoutSeconds));

        var args = McpProtocol.ParseArguments(argumentsJson);
        var response = await _client!.CallToolAsync(toolName, args, cancellationToken: cts.Token).ConfigureAwait(false);

        var text = McpProtocol.ExtractText(response.Content);
        return response.IsError == true
            ? McpCallResponse.Fail(string.IsNullOrWhiteSpace(text) ? $"调用 '{toolName}' 失败（MCP isError）" : text)
            : McpCallResponse.Ok(string.IsNullOrWhiteSpace(text) ? "null" : text);
    }

    /// <summary>
    /// 与具体端点 / 传输无关的入参解析与 content 提取已收敛至 <see cref="McpProtocol"/>（SingleImplementation / TH-12）。
    /// </summary>
    private async Task ConnectCoreAsync(CancellationToken ct)
    {
        if (_connected)
            return;

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = _nodePath,
            Arguments = new[] { _bridgeScriptPath },
            WorkingDirectory = _workingDirectory,
        });

        _client = await McpClient.CreateAsync(transport, new McpClientOptions(), cancellationToken: ct).ConfigureAwait(false);
        _connected = true;
    }

    private void EnsureConnected()
    {
        if (!_connected || _client == null)
            throw new InvalidOperationException("MCPBridge 未连接。请先调用 ConnectAsync()。");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _connected = false;
        _cachedTools = null;

        if (_client != null)
        {
            try
            {
                await _client.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // 释放进程尽力而为，不抛。
            }

            _client = null;
        }
    }
}
