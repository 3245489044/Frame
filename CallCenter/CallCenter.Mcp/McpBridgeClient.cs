using System.IO;

namespace CallCenter.Mcp;

/// <summary>
/// 到 MCPBridge（Node stdio MCP server）的<b>懒连接</b> <see cref="IMcpToolClient"/>（SingleImplementation / TH-12）。
/// 收敛此前 Agent 侧 <c>McpClientManager</c> 的职责：入口脚本解析（未显式给定时经 <see cref="McpBridgeLocator"/> 单一权威定位）、
/// 首次使用才 <see cref="ConnectAsync"/> 拉起 node + stdio（不在构造时触网/拉进程），真实连接委托 <see cref="McpClientConnection"/>。
/// 本类型为纯平台层 MCP 逻辑，不依赖任何 Agent/FileDriven 抽象；调用方负责 DisposeAsync 释放 node 进程。
/// </summary>
public sealed class McpBridgeClient : IMcpToolClient
{
    private const string DefaultNodePath = "node";

    private readonly string _nodePath;
    private readonly string? _bridgeScriptPath; // null → 首次连接经 McpBridgeLocator 定位（单一权威）
    private readonly SemaphoreSlim _connectGate = new(1, 1);

    private McpClientConnection? _connection;
    private bool _disposed;

    /// <param name="nodePath">node 可执行（缺省 "node"）。</param>
    /// <param name="bridgeScriptPath">MCPBridge 入口脚本（可空：未显式给定时于首次连接经 McpBridgeLocator 定位）。</param>
    public McpBridgeClient(string? nodePath = null, string? bridgeScriptPath = null)
    {
        _nodePath = nodePath ?? DefaultNodePath;
        _bridgeScriptPath = bridgeScriptPath;
    }

    /// <summary>是否已建立 MCP 会话。</summary>
    public bool IsConnected => _connection is { IsConnected: true };

    /// <summary>解析入口脚本绝对路径并建立连接（spawn node MCPBridge；入口缺失抛 <see cref="FileNotFoundException"/>）。幂等。</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
        => await EnsureConnectedAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(bool forceRefresh = false, CancellationToken ct = default)
        => await (await EnsureConnectedAsync(ct).ConfigureAwait(false)).ListToolsAsync(forceRefresh, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<McpCallResponse> CallToolAsync(string toolName, string? argumentsJson, CancellationToken ct = default)
        => await (await EnsureConnectedAsync(ct).ConfigureAwait(false)).CallToolAsync(toolName, argumentsJson, ct).ConfigureAwait(false);

    private async Task<McpClientConnection> EnsureConnectedAsync(CancellationToken ct)
    {
        if (_connection is { IsConnected: true })
            return _connection;

        await _connectGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_connection is { IsConnected: true })
                return _connection;

            ThrowIfDisposed();
            _connection = await McpClientConnection.ConnectAsync(ResolveBridgeScript(), _nodePath, ct).ConfigureAwait(false);
            return _connection;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private string ResolveBridgeScript()
    {
        // 显式路径（测试/覆盖用）→ 直接取绝对路径；相对路径以当前目录为基准。
        if (_bridgeScriptPath is not null)
            return Path.GetFullPath(_bridgeScriptPath);

        // 未显式指定：经 CallCenter.Mcp.McpBridgeLocator 单一权威定位（SingleImplementation）。
        var located = McpBridgeLocator.ResolveScriptPath();
        if (located is null)
            throw new FileNotFoundException(
                $"未定位到 MCPBridge 入口脚本（仓库相对 {McpBridgeLocator.BridgeScriptRelativePath}）。请先构建 MCPBridge 项目。",
                McpBridgeLocator.BridgeScriptRelativePath);
        return located;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(McpBridgeClient));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        var c = _connection;
        _connection = null;
        if (c is not null)
        {
            try
            {
                await c.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // 释放进程尽力而为，不抛。
            }
        }

        _connectGate.Dispose();
    }
}
