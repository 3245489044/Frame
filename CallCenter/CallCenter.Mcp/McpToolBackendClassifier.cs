using System.Text.Json;
using CallCenter.Core.Tools;

namespace CallCenter.Mcp;

/// <summary>把 MCP 端点暴露的工具名归类到 CallCenter 终端后端（TypeScript / PowerShell / ExternalMCP）。</summary>
public interface IMcpToolBackendClassifier
{
    string Classify(string toolName);
}

/// <summary>
/// MCPBridge（Node 进程，stdio）的本地工具后端归类：其自带 TS 工具 → TypeScript、<c>invoke_ps_script</c> → PowerShell、
/// 其余（经 MCPBridge 聚合进来的远程 MCP 服务器工具）→ ExternalMCP。MCPBridge 的 ListTools 不携带 origin，
/// 故以「桥自带本地工具名集合」作判定依据（集合与 TypeScriptPrj/TsWorkspace/MCPBridge/tools-definitions/*.json
/// 的 name 字段一致）；如桥的本地工具集变化，经构造入参覆盖即可。
/// </summary>
public sealed class BridgeLocalToolClassifier : IMcpToolBackendClassifier
{
    private readonly IReadOnlySet<string> _tsTools;
    private readonly string _psTool;

    public BridgeLocalToolClassifier(IEnumerable<string>? typeScriptTools = null, string? powerShellTool = null)
    {
        _tsTools = new HashSet<string>(
            typeScriptTools ?? DefaultTypeScriptTools, StringComparer.OrdinalIgnoreCase);
        _psTool = powerShellTool ?? PowerShellToolName;
    }

    /// <summary>
    /// 从桥的 tools-definitions 目录动态读取本地工具名单真源（B4/C2 缓解：新加 TS 工具自动识别，避免与 C# 侧名单双份维护）。
    /// 目录内 *.json 的 name 字段 = 本地工具名；读到非空集合用之，读不到回落内置 <see cref="DefaultTypeScriptTools"/>。
    /// PS 特名仍由 <see cref="PowerShellToolName"/> 单独先判，故全量本地名单直接作为 TS 判定集合即可。
    /// </summary>
    public static BridgeLocalToolClassifier FromLocalToolsDirectory(string directory)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    if (doc.RootElement.ValueKind == JsonValueKind.Object
                        && doc.RootElement.TryGetProperty("name", out var n)
                        && n.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(n.GetString()))
                        names.Add(n.GetString()!);
                }
                catch
                {
                    // 个别坏文件不阻断整体扫描；忽略。
                }
            }
        }

        return new BridgeLocalToolClassifier(names.Count > 0 ? names : null);
    }

    /// <summary>MCPBridge 自带的 PowerShell 调用工具名（tools-definitions/invoke-ps-script.json 的 name）。</summary>
    public const string PowerShellToolName = "invoke_ps_script";

    /// <summary>MCPBridge 自带的本地 TypeScript 工具名（与 tools-definitions/*.json 的 name 一致）。</summary>
    public static readonly string[] DefaultTypeScriptTools =
    {
        "bridge_list", "bridge_status", "bridge_reload", "node_project", "node_select",
    };

    public string Classify(string toolName)
    {
        if (string.Equals(toolName, _psTool, StringComparison.OrdinalIgnoreCase))
            return ToolBackend.PowerShell;
        if (_tsTools.Contains(toolName))
            return ToolBackend.TypeScript;
        return ToolBackend.ExternalMCP;
    }
}
