using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CallCenter.Mcp;

/// <summary>
/// 通用 stdio MCP server 装配（SingleImplementation / TH-12）——把「Host + AddMcpServer + WithStdioServerTransport +
/// WithTools + 日志导向 stderr（避免污染 stdio 协议帧）」的宿主骨架收敛于平台层，供各上层宿主（Agent 瘦宿主 / 注册表宿主等）
/// 以「工具类型集 + 可选的执行端 DI 配置」复用，不再各自复制 Host/AddMcpServer 装配代码。
/// 与承载方式无关：本类只管 stdio 装配，不依赖任何上层/Agent 类型；阻塞运行至关停。
/// </summary>
public static class McpStdioHosting
{
    /// <summary>
    /// 装配并阻塞运行一个 stdio MCP server，暴露 <paramref name="toolTypes"/> 标注的 [McpServerTool] 工具。
    /// <paramref name="configureServices"/> 用于注入工具执行端 DI（如 answer/cline/LLM 网关）。返回 0。
    /// </summary>
    public static async Task<int> RunAsync(
        string[]? args,
        IEnumerable<Type> toolTypes,
        Action<IServiceCollection>? configureServices = null,
        CancellationToken ct = default)
    {
        // MCP stdio server：stdout 仅供 JSON-RPC 协议帧，业务日志一律导向 stderr（LogToStandardErrorThreshold）。
        var builder = Host.CreateApplicationBuilder(args ?? Array.Empty<string>());
        configureServices?.Invoke(builder.Services);
        builder.Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithTools(toolTypes.Distinct().ToArray());
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

        var host = builder.Build();
        await host.RunAsync(ct).ConfigureAwait(false);
        return 0;
    }
}
