using System.Text.Json;
using CallCenter.Core.Tools;
using CallCenter.Mcp;

namespace CallCenter.Tests;

/// <summary>
/// C2 单测：bridge_origins 权威 origin（本地 TS/PS vs 聚合远程）→ 归类；桥不支持时回落名字分类。
/// </summary>
[TestClass]
public class McpBridgeOriginsTests
{
    [TestMethod]
    public void TryParse_Reads_Local_And_Remote()
    {
        var origins = BridgeOrigins.TryParse(
            "{\"local\":[\"node_select\",\"invoke_ps_script\"],\"remote\":[{\"name\":\"remote_tool\",\"serverName\":\"seq\"}]}");
        Assert.IsNotNull(origins);
        CollectionAssert.Contains(new[] { "node_select", "invoke_ps_script" }, origins!.Local.First());
        Assert.AreEqual(1, origins.Remote.Count);
        Assert.AreEqual("remote_tool", origins.Remote[0].Name);
        Assert.AreEqual("seq", origins.Remote[0].ServerName);
    }

    [TestMethod]
    public void TryParse_Empty_Or_NonOrigin_Returns_Null()
    {
        Assert.IsNull(BridgeOrigins.TryParse("{\"local\":[],\"remote\":[]}"), "无本地也无远程 → 视为不可用");
        Assert.IsNull(BridgeOrigins.TryParse("not json at all"));
        Assert.IsNull(BridgeOrigins.TryParse(null));
    }

    [TestMethod]
    public void TryClassifyBackend_Uses_Origin_Not_Name_Guess()
    {
        var origins = BridgeOrigins.TryParse(
            "{\"local\":[\"node_select\",\"invoke_ps_script\"],\"remote\":[{\"name\":\"node_select\",\"serverName\":\"evil_remote\"},{\"name\":\"remote_tool\",\"serverName\":\"seq\"}]}")
            !;

        // 本地名 → TS/PS
        Assert.AreEqual(ToolBackend.TypeScript, origins.TryClassifyBackend("node_select"));
        Assert.AreEqual(ToolBackend.PowerShell, origins.TryClassifyBackend("invoke_ps_script"));
        // 远程名 → ExternalMCP
        Assert.AreEqual(ToolBackend.ExternalMCP, origins.TryClassifyBackend("remote_tool"));
        // 未知 → null（回落名字分类）
        Assert.IsNull(origins.TryClassifyBackend("some_unknown"));
    }

    [TestMethod]
    public async Task Discover_With_Origins_Classifies_By_Authority()
    {
        var client = new OriginFakeClient(originsSupported: true);
        var descs = await McpBridgeDiscovery.DiscoverDescriptorsAsync(client, new BridgeLocalToolClassifier());

        var local = descs.First(d => d.Name == "node_select");
        var ps = descs.First(d => d.Name == "invoke_ps_script");
        var remote = descs.First(d => d.Name == "remote_tool");

        Assert.AreEqual(ToolBackend.TypeScript, local.Backend);
        Assert.IsTrue(local.Flags.HasFlag(ToolFlags.Deterministic));
        Assert.AreEqual(ToolBackend.PowerShell, ps.Backend);
        Assert.AreEqual(ToolBackend.ExternalMCP, remote.Backend);
        Assert.IsTrue(remote.Flags.HasFlag(ToolFlags.Externalized));
    }

    [TestMethod]
    public async Task Discover_Without_Origins_Falls_Back_To_Name_Classifier()
    {
        // 旧版桥无 bridge_origins（IsError）→ 回落名字分类。
        var client = new OriginFakeClient(originsSupported: false);
        var descs = await McpBridgeDiscovery.DiscoverDescriptorsAsync(client, new BridgeLocalToolClassifier());

        Assert.AreEqual(ToolBackend.TypeScript, descs.First(d => d.Name == "node_select").Backend);
        Assert.AreEqual(ToolBackend.ExternalMCP, descs.First(d => d.Name == "some_remote").Backend);
    }

    /// <summary>内存客户端：可控是否支持 bridge_origins。</summary>
    private sealed class OriginFakeClient : IMcpToolClient
    {
        private readonly bool _originsSupported;

        public OriginFakeClient(bool originsSupported) => _originsSupported = originsSupported;

        public McpToolInfo[] DiscoveredTools { get; } =
        {
            new("node_select", "选择 Node 上下文", default),
            new(BridgeLocalToolClassifier.PowerShellToolName, "执行 PowerShell", default),
            new("remote_tool", "聚合远程工具", default),
            new("some_remote", "未知远程", default),
        };

        public Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(bool forceRefresh = false, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<McpToolInfo>>(DiscoveredTools);

        public Task<McpCallResponse> CallToolAsync(string toolName, string? argumentsJson, CancellationToken ct = default)
        {
            if (toolName == McpBridgeDiscovery.OriginsToolName)
            {
                if (!_originsSupported) return Task.FromResult(McpCallResponse.Fail("未知工具"));
                return Task.FromResult(McpCallResponse.Ok(
                    "{\"local\":[\"node_select\",\"invoke_ps_script\"],\"remote\":[{\"name\":\"remote_tool\",\"serverName\":\"seq\"}]}"));
            }

            return Task.FromResult(McpCallResponse.Ok("echo"));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
