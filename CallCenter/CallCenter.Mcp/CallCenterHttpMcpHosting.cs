using CallCenter.Core.Router;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting; // WebHost.UseUrls
using Microsoft.Extensions.DependencyInjection; // AddMcpServer（IServiceCollection 扩展）
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server; // WithTools/WithHttpTransport


namespace CallCenter.Mcp;

/// <summary>
/// CallCenter HTTP MCP server 承载（服务端方向 / 同进程非阻塞托管）—— 把注入的 <see cref="ToolRouter"/> 统一工具面
/// 以官方 ModelContextProtocol 暴露为 Streamable HTTP <c>/mcp</c> 端点（默认 <c>http://localhost:{port}/mcp</c>）。
/// <para>
/// <b>承载收拢位置</b>：HTTP MCP server 承载从 Agent 侧（McpServerHosting/RegistryMcpHosting）收拢至 CallCenter（平台层），
/// Agent/FileDriven 等上层宿主不再自行持有 HTTP MCP server；本类即承载实现（唯一）。上层宿主只做<b>接线</b>——
/// 构造一个 <see cref="ToolRouter"/>（含其能力工具）后经 <see cref="StartHostedHttpAsync"/> / <see cref="RunHttpAsync"/> 拉起端点，
/// 并经本机 Loopback MCP client（指向本端点）调用。工具集经 <see cref="CallCenterMcpTools.Inject"/> 静态注入（照抄 Agent
/// RegistryMcpTools「静态真源」已验证形态，确保 WithTools 稳定注册）；不反引用任何 Agent 类型（9.4 D7：平台不依赖产品）。
/// </para>
/// <para>生命周期：返回已 <c>StartAsync</c> 的 <see cref="WebApplication"/>（实现 <see cref="IHost"/>），调用方持有
/// Start/Stop/Dispose。绑定 localhost（仅回环），无鉴权——仅限本机受信客户端。</para>
/// </summary>
public static class CallCenterHttpMcpHosting
{
    /// <summary>
    /// 同进程非阻塞托管本机 HTTP MCP server（承载收拢入口）：把 <paramref name="router"/> 工具面暴露为
    /// <c>http://localhost:{defaultPort}{path}</c>（defaultPort=0 → 预留空闲端口）。返回已 Start 的
    /// <see cref="WebApplication"/>，调用方持有生命周期。启动前把 router 注入 <see cref="CallCenterMcpTools"/>（静态真源）。
    /// </summary>
    public static async Task<WebApplication> StartHostedHttpAsync(
        ToolRouter router,
        int defaultPort = 0,
        string path = "/mcp")
    {
        var port = defaultPort <= 0 ? ReserveFreePort() : defaultPort;
        CallCenterMcpTools.Inject(router); // 静态真源注入（承载工具面唯一装配点）
        var app = BuildHttpApplication(port, path);
        await app.StartAsync().ConfigureAwait(false);
        return app;
    }

    /// <summary>阻塞运行（独立承载进程/CLI --http 用）。</summary>
    public static async Task<int> RunHttpAsync(
        ToolRouter router,
        int defaultPort = 0,
        string path = "/mcp")
    {
        var app = await StartHostedHttpAsync(router, defaultPort, path).ConfigureAwait(false);
        await app.WaitForShutdownAsync().ConfigureAwait(false);
        return 0;
    }

    /// <summary>构建本机 Streamable HTTP MCP server（不启动）。工具类型 = <see cref="CallCenterMcpTools"/>（静态工具）。</summary>
    private static WebApplication BuildHttpApplication(int port, string path)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://localhost:{port}");

        builder.Services.AddMcpServer()
            .WithHttpTransport()
            .WithTools(new[] { typeof(CallCenterMcpTools) });

        var app = builder.Build();
        app.MapMcp(path); // 挂载 /mcp 端点
        return app;
    }

    /// <summary>预留一个空闲本机端口（port=0 语义；避免多承载/测试并发抢同一端口）。</summary>
    private static int ReserveFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
