using CallCenter.Core.Tools;
using CallCenter.Core.Tracks;

namespace CallCenter.Mcp;

/// <summary>
/// MCPBridge 终端轨（Node MCPBridge，stdio）：把经 <see cref="IMcpToolClient"/> 发现的工具投影为工具描述符，
/// 按名把 JSON 入参经 MCP <c>CallTool</c> 路由执行并返回文本结果。
/// <para>
/// 轨级 <see cref="Backend"/> 用独立轨道键 <see cref="GroupBackend"/>（"McpBridge"）作 Router 分轨/版本化替换的
/// 分组键（W4/C4：不与 <see cref="ToolBackend.ExternalMCP"/> 混用，避免日后独立的 ExternalMCP 轨在热换本轨时被误删）；
/// 工具描述符各自携带 backend（TypeScript/PowerShell/ExternalMCP）供统一工具面与 Catalog 区分 TS/PS/外部轨，
/// 两者不冲突。
/// </para>
/// </summary>
public sealed class McpBridgeTrack : ICallableTrack
{
    /// <summary>本轨作为 Router 分组/版本化替换的轨道键（区别于逐工具描述符的 backend 分类）。</summary>
    public const string GroupBackend = "McpBridge";

    private readonly IMcpToolClient _client;
    private readonly IReadOnlyList<ToolDescriptor> _tools;
    private readonly IReadOnlyDictionary<string, ToolDescriptor> _byName;

    /// <summary>以已发现的工具集构造（工具发现须在连接后由调用方完成一次）。</summary>
    public McpBridgeTrack(IMcpToolClient client, IEnumerable<ToolDescriptor> tools, string? backend = null)
    {
        _client = client;
        _tools = tools?.ToArray() ?? Array.Empty<ToolDescriptor>();
        _byName = _tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        Backend = backend ?? GroupBackend;
    }

    /// <summary>本轨后端（ExternalMCP 分组键；具体工具 backend 见各自描述符）。</summary>
    public string Backend { get; }

    /// <summary>本轨可调用工具集（已投影描述符）。</summary>
    public IReadOnlyCollection<ToolDescriptor> Tools => _tools;

    /// <summary>仅对发现到的工具名返回 true。</summary>
    public bool CanCall(string name) => _byName.ContainsKey(name);

    /// <inheritdoc />
    public async Task<ToolExecutionResult> CallAsync(string name, string? argumentsJson, CancellationToken ct = default)
    {
        if (!_byName.ContainsKey(name))
            return ToolExecutionResult.Fail($"McpBridge 轨无名为 '{name}' 的工具");

        ct.ThrowIfCancellationRequested();

        var resp = await _client.CallToolAsync(name, argumentsJson, ct).ConfigureAwait(false);
        return resp.IsError
            ? ToolExecutionResult.Fail(resp.Text ?? $"调用 '{name}' 失败（MCP 返回错误）")
            : ToolExecutionResult.Ok(resp.Text ?? "null");
    }
}
