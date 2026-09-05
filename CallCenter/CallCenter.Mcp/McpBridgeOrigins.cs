using System.Text.Json;
using CallCenter.Core.Tools;

namespace CallCenter.Mcp;

/// <summary>聚合远程工具来源条目（bridge_origins 返回的 remote 项）。</summary>
public sealed record BridgeOriginRemote(string Name, string? ServerName);

/// <summary>
/// 桥权威 origin 映射（C2）：{ local: 本地工具名集, remote: 聚合远程工具[名→归属服务器] }。
/// 由桥的 <c>bridge_origins</c> 元工具返回（McpBridge.ORIGINS_TOOL），供消费方据此归类，
/// 避免靠工具名猜测本地 TS/PS vs 聚合远程造成误判。工具不存在（旧版桥）时为 null → 回落名字分类。
/// </summary>
public sealed record BridgeOrigins(
    IReadOnlySet<string> Local,
    IReadOnlyList<BridgeOriginRemote> Remote)
{
    /// <summary>解析桥返回的 origin JSON；非对象/空（无本地也无远程）→ null（视为不可用，回落）。</summary>
    public static BridgeOrigins? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var local = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("local", out var l) && l.ValueKind == JsonValueKind.Array)
                foreach (var item in l.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                        local.Add(item.GetString()!);

            var remote = new List<BridgeOriginRemote>();
            if (root.TryGetProperty("remote", out var r) && r.ValueKind == JsonValueKind.Array)
                foreach (var item in r.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    if (!item.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String) continue;
                    var server = item.TryGetProperty("serverName", out var s) && s.ValueKind == JsonValueKind.String
                        ? s.GetString() : null;
                    remote.Add(new BridgeOriginRemote(n.GetString()!, server));
                }

            if (local.Count == 0 && remote.Count == 0) return null;
            return new BridgeOrigins(local, remote);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 按权威 origin 归类后端：本地名 → TypeScript（invoke_ps_script 除外 → PowerShell）；远程名 → ExternalMCP；
    /// 均不匹配 → null（调用方回落名字分类）。注：本地与远程同名的重复条目无法仅凭名区分，属 SDK 传输限制。
    /// </summary>
    public string? TryClassifyBackend(string toolName)
    {
        if (Local.Contains(toolName))
            return string.Equals(toolName, BridgeLocalToolClassifier.PowerShellToolName, StringComparison.OrdinalIgnoreCase)
                ? ToolBackend.PowerShell
                : ToolBackend.TypeScript;

        foreach (var entry in Remote)
            if (string.Equals(entry.Name, toolName, StringComparison.OrdinalIgnoreCase))
                return ToolBackend.ExternalMCP;

        return null;
    }
}

/// <summary>桥工具发现的 origin 辅助（C2）。经 <c>bridge_origins</c> 拿权威 origin 归类；工具不存在/解析失败回落名字分类。</summary>
public static class McpBridgeDiscovery
{
    /// <summary>桥权威来源查询工具名（对应 TS McpBridge.ORIGINS_TOOL）。</summary>
    public const string OriginsToolName = "bridge_origins";

    /// <summary>尝试读取桥权威 origin 映射；桥不支持（旧版/错误）返回 null。</summary>
    public static async Task<BridgeOrigins?> TryReadOriginsAsync(IMcpToolClient client, CancellationToken ct = default)
    {
        try
        {
            var resp = await client.CallToolAsync(OriginsToolName, null, ct).ConfigureAwait(false);
            return resp.IsError ? null : BridgeOrigins.TryParse(resp.Text);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 统一投影桥工具为描述符：先读权威 origin（bridge_origins），有则按其归类；无则回落 <paramref name="fallback"/>（名字分类）。
    /// </summary>
    public static async Task<ToolDescriptor[]> DiscoverDescriptorsAsync(
        IMcpToolClient client,
        IMcpToolBackendClassifier? fallback = null,
        CancellationToken ct = default)
    {
        fallback ??= new BridgeLocalToolClassifier();
        var origins = await TryReadOriginsAsync(client, ct).ConfigureAwait(false);

        var tools = await client.ListToolsAsync(ct: ct).ConfigureAwait(false);
        var result = new List<ToolDescriptor>(tools.Count);
        foreach (var t in tools)
        {
            var backend = origins?.TryClassifyBackend(t.Name) ?? fallback.Classify(t.Name);
            result.Add(McpToolMapper.ToDescriptor(t, backend));
        }

        return result.ToArray();
    }
}
