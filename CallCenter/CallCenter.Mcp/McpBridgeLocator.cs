using System.IO;

namespace CallCenter.Mcp;

/// <summary>
/// MCPBridge 入口脚本（Node stdio MCP server）的仓库相对路径与定位单一权威（SingleImplementation / TH-12）。
/// 收敛此前散在 CallCenter.Host 的 <c>LocateMcpBridgeScript</c>（自写相对路径+逐级上溯）与 Agent.Axioms
/// 中同名重复常量的两份定义；Host 与 Agent（McpBridgeClient）一律经本类解析，禁止各自写死路径。
/// </summary>
public static class McpBridgeLocator
{
    /// <summary>MCPBridge 入口脚本相对仓库根的路径（node 构建产出 build/index.js）。</summary>
    public const string BridgeScriptRelativePath =
        @"TypeScriptPrj\TsWorkspace\MCPBridge\build\index.js";

    /// <summary>
    /// 从 <paramref name="searchStart"/>（缺省当前程序输出目录）逐级上溯，定位真实存在的 <c>build/index.js</c> 绝对路径；
    /// 未找到返回 null（调用方按需提示先构建 MCPBridge 项目）。
    /// </summary>
    public static string? ResolveScriptPath(string? searchStart = null)
    {
        var dir = new DirectoryInfo(searchStart ?? AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, BridgeScriptRelativePath);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// 解析 MCPBridge 本地工具定义目录（tools-definitions/*.json，name 字段 = 桥自带本地工具名单真源），
    /// 供分类器动态读取（B4：避免 C# 侧硬编码与 TS 定义双份维护）。以定位到的入口脚本同层推导；
    /// 目录不存在返回 null。
    /// </summary>
    public static string? ResolveToolsDefinitionsDirectory(string? scriptPath = null)
    {
        var script = scriptPath ?? ResolveScriptPath();
        if (script is null) return null;

        var buildDir = Path.GetDirectoryName(script);      // .../MCPBridge/build
        var mcpBridgeDir = Path.GetDirectoryName(buildDir); // .../MCPBridge
        if (string.IsNullOrEmpty(mcpBridgeDir)) return null;

        var toolsDefs = Path.Combine(mcpBridgeDir, "tools-definitions");
        return Directory.Exists(toolsDefs) ? toolsDefs : null;
    }
}
