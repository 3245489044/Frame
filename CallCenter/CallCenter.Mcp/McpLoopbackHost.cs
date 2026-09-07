using CallCenter.Core.Router;
using Microsoft.AspNetCore.Builder;

namespace CallCenter.Mcp;

/// <summary>
/// 进程内<b>受管本机 MCP 回环宿主</b>（SingleImplementation / TH-12）——把通用承载生命周期收拢于平台层：
/// 把一个注入的 <see cref="ToolRouter"/> 统一工具面经 <see cref="CallCenterHttpMcpHosting"/> 暴露为 Streamable HTTP <c>/mcp</c>，
/// 并经 <see cref="CallCenterHttpMcpClient"/> 回连本机端点，向调用方返回就绪的回环 <see cref="IMcpToolClient"/>。
/// 收敛上层宿主（如 Agent 的 AgentMcpLoopback）自行维护的「懒起承载 / 预留端口 / 回连 / 幂等 / 释放」逻辑，
/// 上层只<b>接线</b>（构造 router → 拿 client → 释放）。
/// </summary>
public sealed class McpLoopbackHost : IAsyncDisposable
{
    private readonly ToolRouter _router;
    private readonly int _defaultPort;   // <=0 → 预留空闲端口
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private WebApplication? _app;
    private IMcpToolClient? _client;
    private bool _disposed;

    /// <param name="router">本机承载要暴露的统一工具面（含各轨能力）。</param>
    /// <param name="defaultPort">缺省监听端口；&lt;=0 → 预留空闲端口（本机回环，无鉴权，仅供受信本机 client）。</param>
    /// <param name="path">MCP 端点路径（缺省 /mcp）。</param>
    public McpLoopbackHost(ToolRouter router, int defaultPort = 0, string path = "/mcp")
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _defaultPort = defaultPort;
        _path = string.IsNullOrWhiteSpace(path) ? "/mcp" : path;
    }

    /// <summary>当前就绪的回环 client（未启动为 null）。</summary>
    public IMcpToolClient? Client => _client;

    /// <summary>本机承载与回环 client 是否已启动。</summary>
    public bool IsStarted => _client is not null;

    /// <summary>懒启动承载并回连本机端点，返回就绪回环 <see cref="IMcpToolClient"/>（幂等、线程安全）。</summary>
    public async Task<IMcpToolClient> GetClientAsync(CancellationToken ct = default)
    {
        if (_client is not null)
            return _client;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_client is not null)
                return _client;

            ThrowIfDisposed();

            var port = _defaultPort <= 0 ? ReserveFreePort() : _defaultPort;
            var app = await CallCenterHttpMcpHosting.StartHostedHttpAsync(_router, defaultPort: port, path: _path).ConfigureAwait(false);
            IMcpToolClient? client = null;
            try
            {
                client = await CallCenterHttpMcpClient.ConnectAsync(new Uri($"http://localhost:{port}{_path}"), ct: ct).ConfigureAwait(false);
            }
            catch
            {
                // 回连失败：释放已起的承载，避免端口/进程泄漏，再向上抛。
                try { await app.DisposeAsync().ConfigureAwait(false); } catch { /* 尽力而为 */ }
                throw;
            }

            _app = app;
            _client = client;
            return client;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>停止承载并释放回环 client（幂等；停止后再次 <see cref="GetClientAsync"/> 可重启）。</summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        WebApplication? app;
        IMcpToolClient? client;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            app = _app;
            client = _client;
            _app = null;
            _client = null;
        }
        finally
        {
            _gate.Release();
        }

        if (client is not null)
        {
            try { await client.DisposeAsync().ConfigureAwait(false); } catch { /* 尽力而为 */ }
        }
        if (app is not null)
        {
            try { await app.StopAsync(ct).ConfigureAwait(false); } catch { /* 尽力而为 */ }
            try { await app.DisposeAsync().ConfigureAwait(false); } catch { /* 尽力而为 */ }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(McpLoopbackHost));
    }

    /// <summary>预留一个空闲本机端口（port=0 语义；避免并发多承载抢同一端口）。</summary>
    private static int ReserveFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
