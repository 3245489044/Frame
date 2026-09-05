using System.Text.Json;
using CallCenter.Core.Router;
using CallCenter.Core.Schema;
using CallCenter.Core.Tools;
using CallCenter.Core.Tracks;
using CallCenter.Mcp;

namespace CallCenter.Tests;

/// <summary>
/// P3 TS/PS 轨（MCPBridge 终端轨）回归闸：分类器把桥本地 TS/PS 工具归到 TypeScript/PowerShell、其余归 ExternalMCP；
/// 映射器纯投影描述符；McpBridgeTrack 经内存 IMcpToolClient 驱动（不拉起 node）实现按名路由/执行/错误映射，
/// 并能经 ToolRouter 与既有 C#Track 并列组成统一工具面。
/// </summary>
[TestClass]
public class McpBridgeTrackTests
{
    // ─── 分类器（纯函数）────────────────────────────────────────────

    [TestMethod]
    public void Classifier_Maps_Local_Ps_And_Ts_Tools_To_Their_Backends()
    {
        var c = new BridgeLocalToolClassifier();

        Assert.AreEqual(ToolBackend.PowerShell, c.Classify(BridgeLocalToolClassifier.PowerShellToolName));
        foreach (var ts in BridgeLocalToolClassifier.DefaultTypeScriptTools)
            Assert.AreEqual(ToolBackend.TypeScript, c.Classify(ts));
    }

    [TestMethod]
    public void Classifier_Falls_Back_To_ExternalMcp_For_Unknown_Tool()
    {
        var c = new BridgeLocalToolClassifier();
        Assert.AreEqual(ToolBackend.ExternalMCP, c.Classify("some_aggregated_remote_tool"));
    }

    [TestMethod]
    public void Classifier_Reads_Local_Names_From_ToolsDefinitions_Dir()
    {
        // B4：名单真源在 tools-definitions/*.json —— 新加本地 TS 工具经目录扫描自动识别，不再依赖 C# 硬编码名单。
        var dir = Path.Combine(Path.GetTempPath(), "mcpdefs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "new_ts.json"), "{\"name\":\"new_tool_alpha\",\"handler\":\"new-tool-alpha\"}");
            File.WriteAllText(Path.Combine(dir, "another.json"), "{\"name\":\"another_local\"}");
            File.WriteAllText(Path.Combine(dir, "broken.json"), "{ not json");

            var c = BridgeLocalToolClassifier.FromLocalToolsDirectory(dir);
            Assert.AreEqual(ToolBackend.TypeScript, c.Classify("new_tool_alpha"), "目录扫描到的新本地 TS 工具应归 TypeScript");
            Assert.AreEqual(ToolBackend.ExternalMCP, c.Classify("some_remote"), "未列本地名单的仍应归 ExternalMCP");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    // ─── 映射器（纯投影）────────────────────────────────────────────

    [TestMethod]
    public void Mapper_Projects_McpToolInfo_To_Descriptor_With_Backend_And_Flags()
    {
        var tool = new McpToolInfo("invoke_ps_script", "执行 PowerShell 脚本", default);

        var d = McpToolMapper.ToDescriptor(tool, ToolBackend.PowerShell);

        Assert.AreEqual("invoke_ps_script", d.Name);
        Assert.AreEqual($"tool:{ToolBackend.PowerShell}:invoke_ps_script", d.Id);
        Assert.AreEqual(ToolBackend.PowerShell, d.Backend);
        Assert.IsTrue(d.Flags.HasFlag(ToolFlags.Deterministic));
        Assert.IsFalse(d.Flags.HasFlag(ToolFlags.Externalized));
    }

    [TestMethod]
    public void Mapper_Marks_ExternalMcp_Backend_As_Externalized()
    {
        var tool = new McpToolInfo("remote_tool", "聚合的远程服务器工具", default);

        var d = McpToolMapper.ToDescriptor(tool, ToolBackend.ExternalMCP);

        Assert.IsTrue(d.Flags.HasFlag(ToolFlags.Externalized));
        Assert.IsFalse(d.Flags.HasFlag(ToolFlags.Deterministic));
    }

    [TestMethod]
    public void Mapper_Collapses_Mcp_JsonSchema_Into_Param_Meta_And_Maps_To_Model_Schema()
    {
        // X-0：真实 MCP JSON-Schema（properties + required）→ 规范参数形状（不再把顶层键当参数名），
        // 且 ToolFunctionSchemaMapper 能据此导出正确的模型 schema（type/required/description）。
        using var doc = JsonDocument.Parse(
            "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"目标路径\"},\"depth\":{\"type\":\"integer\"},\"opt\":{\"type\":\"boolean\"}},\"required\":[\"path\"]}");
        var tool = new McpToolInfo("node_select", "desc", doc.RootElement);
        var descriptor = McpToolMapper.ToDescriptor(tool, ToolBackend.TypeScript);

        var meta = descriptor.InputSchema;
        Assert.AreEqual(3, meta.Count);
        AssertMeta(meta, "path", "string", optional: false);
        AssertMeta(meta, "depth", "integer", optional: true);
        AssertMeta(meta, "opt", "boolean", optional: true);

        var exposed = ToolFunctionSchemaMapper.MapExposed(new[] { descriptor });
        Assert.AreEqual(1, exposed.Count);
        var root = JsonDocument.Parse(exposed[0].InputSchema.GetRawText()).RootElement;
        Assert.AreEqual("path", root.GetProperty("required")[0].GetString(), "required 应只含 path");
        Assert.AreEqual("string", root.GetProperty("properties").GetProperty("path").GetProperty("type").GetString());
        Assert.AreEqual("目标路径", root.GetProperty("properties").GetProperty("path").GetProperty("description").GetString());
        Assert.AreEqual("integer", root.GetProperty("properties").GetProperty("depth").GetProperty("type").GetString());
    }

    private static void AssertMeta(IReadOnlyDictionary<string, object?> meta, string name, string expectedType, bool optional)
    {
        Assert.IsTrue(meta.TryGetValue(name, out var raw), $"缺少参数 {name}");
        Assert.IsTrue(raw is Dictionary<string, object?>, $"{name} 元数据形状应为字典");
        var m = (Dictionary<string, object?>)raw!;
        Assert.AreEqual(expectedType, m["type"] as string);
        Assert.AreEqual(optional, m["optional"] is bool ob && ob);
    }

    // ─── 终端轨（内存客户端驱动，不拉起 node）────────────────────────

    [TestMethod]
    public void Track_GroupKey_Is_Independent_Of_ExternalMCP()
    {
        Assert.AreEqual(McpBridgeTrack.GroupBackend, BuildTrack().Backend);
        Assert.AreNotEqual(ToolBackend.ExternalMCP, McpBridgeTrack.GroupBackend, "轨级分组键须与逐工具 backend 分类解耦（W4/C4）");
    }

    [TestMethod]
    public void ReplaceMcpBridge_Track_Leaves_Separate_ExternalMcp_Track_Intact()
    {
        // C4：McpBridge 以独立分组键热换，不应误删独立的 ExternalMCP 轨（ReplaceTrack 按同 backend 整组替换）。
        var router = new ToolRouter();
        router.AddTrack(new StubTrack(ToolBackend.ExternalMCP, Simple("ext_only", ToolBackend.ExternalMCP)));
        router.AddTrack(BuildTrack()); // McpBridge（GroupBackend）

        router.ReplaceTrack(BuildTrack()); // 热换 McpBridge

        Assert.IsTrue(router.Tools.Any(t => t.Name == "ext_only" && t.Backend == ToolBackend.ExternalMCP),
            "独立 ExternalMCP 轨不应被 McpBridge 热换误删");
        Assert.IsTrue(router.Tools.Any(t => t.Name == "echo_tool"), "新 McpBridge 轨工具应生效");
    }

    [TestMethod]
    public void CanCall_Only_For_Discovered_Tools()
    {
        var track = BuildTrack();
        Assert.IsTrue(track.CanCall("node_select"));
        Assert.IsTrue(track.CanCall("invoke_ps_script"));
        Assert.IsFalse(track.CanCall("does_not_exist"));
    }

    [TestMethod]
    public async Task CallAsync_Routes_To_Client_And_Returns_Text_As_Json()
    {
        var track = BuildTrack();
        var result = await track.CallAsync("echo_tool", "{\"text\":\"hi\"}");

        Assert.IsTrue(result.Success);
        Assert.AreEqual("echo: {\"text\":\"hi\"}", result.OutputJson);
    }

    [TestMethod]
    public async Task CallAsync_Maps_Mcp_Error_To_Failure()
    {
        var track = BuildTrack();
        var result = await track.CallAsync("failing_tool", null);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "boom");
    }

    [TestMethod]
    public async Task Router_Adds_McpBridge_Track_Alongside_CSharp_Tracks()
    {
        // MCPBridge 轨与（本测试不构造真实 C# 轨时的）空路由并列：证明可按名路由到 Mcp 轨。
        var router = new ToolRouter();
        router.AddTrack(BuildTrack());

        Assert.IsTrue(router.CanCall("node_select"));
        var result = await router.CallAsync("echo_tool", "{\"text\":\"ok\"}");
        Assert.IsTrue(result.Success);
        Assert.AreEqual("echo: {\"text\":\"ok\"}", result.OutputJson);
    }

    [TestMethod]
    public void Mapper_Handles_Missing_Description_And_No_Schema()
    {
        var tool = new McpToolInfo("bare", null, default);
        var d = McpToolMapper.ToDescriptor(tool, ToolBackend.TypeScript);

        Assert.AreEqual("bare", d.Description);
        Assert.AreEqual(0, d.InputSchema.Count);
    }

    private static McpBridgeTrack BuildTrack()
    {
        var client = new FakeMcpToolClient();
        var classifier = new BridgeLocalToolClassifier();
        var tools = client.DiscoveredTools
            .Select(t => McpToolMapper.ToDescriptor(t, classifier.Classify(t.Name)))
            .ToArray();
        return new McpBridgeTrack(client, tools);
    }

    private static ToolDescriptor Simple(string name, string backend)
        => new($"tool:{backend.ToLowerInvariant()}:{name}", name, backend, 1, "fake",
            new Dictionary<string, object?>(), new Dictionary<string, object?>(), ToolFlags.Externalized);

    /// <summary>固定 backend + 单工具的轨桩（用于验证 ReplaceTrack 分组互不误删）。</summary>
    private sealed class StubTrack : ICallableTrack
    {
        private readonly ToolDescriptor _tool;

        public StubTrack(string backend, ToolDescriptor tool)
        {
            Backend = backend;
            _tool = tool;
        }

        public string Backend { get; }

        public IReadOnlyCollection<ToolDescriptor> Tools => new[] { _tool };

        public bool CanCall(string name) => string.Equals(name, _tool.Name, StringComparison.OrdinalIgnoreCase);

        public Task<ToolExecutionResult> CallAsync(string name, string? argumentsJson, CancellationToken ct = default)
            => Task.FromResult(ToolExecutionResult.Fail("stub"));
    }

    /// <summary>内存 MCP 客户端：预置 TS/PS/未知/错误工具，回显式执行。</summary>
    private sealed class FakeMcpToolClient : IMcpToolClient
    {
        public McpToolInfo[] DiscoveredTools { get; } =
        {
            new("node_select", "选择 Node 上下文", default),
            new("node_project", "Node 项目", default),
            new("bridge_status", "桥状态", default),
            new("bridge_list", "桥列表", default),
            new("bridge_reload", "桥重载", default),
            new(BridgeLocalToolClassifier.PowerShellToolName, "执行 PowerShell 脚本", default),
            new("remote_tool", "聚合的远程服务器工具", default),
            new("echo_tool", "回显测试工具", default),
            new("failing_tool", "恒失败测试工具", default),
        };

        public Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(bool forceRefresh = false, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<McpToolInfo>>(DiscoveredTools);

        public Task<McpCallResponse> CallToolAsync(string toolName, string? argumentsJson, CancellationToken ct = default)
        {
            if (toolName == "failing_tool")
                return Task.FromResult(McpCallResponse.Fail("boom"));

            var arg = string.IsNullOrWhiteSpace(argumentsJson) ? "(无参)" : argumentsJson;
            return Task.FromResult(McpCallResponse.Ok($"echo: {arg}"));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
