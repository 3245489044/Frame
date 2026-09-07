using System.ComponentModel;
using System.Text.Json;
using CallCenter.Core.Router;
using CallCenter.Core.Schema;
using CallCenter.Core.Tools;
using ModelContextProtocol.Server;

namespace CallCenter.Mcp;

/// <summary>
/// CallCenter HTTP MCP server 承载的工具面（服务端方向）—— 把 <see cref="ToolRouter"/> 统一工具面
/// （跨语言多轨：C# / TS / PS / ExternalMCP 的 <see cref="ToolDescriptor"/> 集）经官方 ModelContextProtocol
/// 以 registry 双入口（<c>cc_list_tools</c> / <c>cc_call_tool</c>）暴露，供外部 LLM（Cline/Claude/子智能体）与本机
/// Loopback MCP client 经同一 /mcp 端点访问。
/// <para>
/// 镜像仓库已验证的「注册表驱动双工具入口」形态（Agent <c>RegistryMcpTools</c>：纯静态工具 + 静态真源、全基础类型参数）：
/// <see cref="ToolRouter"/> 经 <see cref="Inject"/>（承载装配时一次性注入，组合根接线）静态持有，工具方法零 DI / 零
/// CancellationToken —— 确保官方 SDK <c>WithTools</c> 扫描能稳定注册为 server tools handler（tools/list 可枚举）。承载载体
/// = CallCenter <see cref="ToolRouter"/>（平台层、Agent 无关），不反引用任何 Agent 类型（9.4 D7）。
/// schema 经 <see cref="ToolFunctionSchemaMapper"/> 从 <see cref="ToolDescriptor.InputSchema"/> 派生；引擎确定性核心
/// （<see cref="ToolFlags.NotExposedToModel"/>）默认不进 list，防递归暴露。
/// </para>
/// </summary>
[McpServerToolType]
public sealed class CallCenterMcpTools
{
    private static ToolRouter? _router;

    /// <summary>注入承载工具面真源（组合根接线；重复注入覆盖）。未注入时 list 返回空、call 返回未装配诊断。</summary>
    public static void Inject(ToolRouter router) => _router = router ?? throw new ArgumentNullException(nameof(router));

    /// <summary>列出承载工具面中可经 MCP 调用的工具（NotExposedToModel 过滤）。</summary>
    [McpServerTool, Description("列出 CallCenter 统一工具面中可调用的工具（跨 C#/TS/PS/ExternalMCP 多轨聚合；确定性核心按 NotExposedToModel 过滤不进模型 schema）。")]
    public static Task<string> CcListTools(
        [Description("可选：按工具名过滤（子串匹配，忽略大小写）。")] string? prefix = null,
        [Description("可选：true 只返回名/描述（跳过 schema 细节），快速概览。")] bool schemaOnly = false)
    {
        var router = _router;
        if (router is null)
            return Task.FromResult(JsonSerializer.Serialize(new { tools = Array.Empty<object>(), total = 0, message = "（未装配）CallCenter 工具面未注入。" }));

        var rows = (prefix is null or { Length: 0 } ? router.Tools : router.Tools.Where(t => t.Name.Contains(prefix, StringComparison.OrdinalIgnoreCase)))
            .Where(t => !t.Flags.HasFlag(ToolFlags.NotExposedToModel))
            .Select(t => schemaOnly
                ? (object)new { name = t.Name, description = t.Description, backend = t.Backend }
                : (object)new { name = t.Name, description = t.Description, backend = t.Backend, inputSchema = BuildSchema(t) })
            .ToList();

        return Task.FromResult(JsonSerializer.Serialize(new { tools = rows, total = rows.Count }));
    }

    /// <summary>按名调用承载工具面中的一个工具。arguments 为 JSON 对象，键对应工具参数名。</summary>
    [McpServerTool, Description("按名调用 CallCenter 统一工具面中的一个工具（经 ToolRouter 路由到对应轨执行）。arguments 为 JSON 对象字符串，键对应该工具参数名。工具清单见 cc_list_tools。")]
    public static async Task<string> CcCallTool(
        [Description("要调用的工具名（须先经 cc_list_tools 确认存在且非 NotExposedToModel）。")] string toolName,
        [Description("参数 JSON 对象字符串（如 {\"text\":\"hi\"}）；无参可省略。")] string arguments = "{}")
    {
        var router = _router;
        if (router is null)
            return JsonSerializer.Serialize(new { status = "error", message = "（未装配）CallCenter 工具面未注入。", tool = toolName });

        if (!router.CanCall(toolName))
            return JsonSerializer.Serialize(new { status = "error", message = $"CallCenter 工具面无工具 '{toolName}'。请先 cc_list_tools 确认。", tool = toolName });

        var result = await router.CallAsync(toolName, string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments, CancellationToken.None).ConfigureAwait(false);
        return result.Success
            ? (result.OutputJson ?? "null")
            : JsonSerializer.Serialize(new { status = "error", tool = toolName, message = result.Error });
    }

    private static JsonElement BuildSchema(ToolDescriptor tool)
    {
        var mapped = ToolFunctionSchemaMapper.MapExposed(new[] { tool });
        return mapped.Count > 0 ? mapped[0].InputSchema : default;
    }
}
