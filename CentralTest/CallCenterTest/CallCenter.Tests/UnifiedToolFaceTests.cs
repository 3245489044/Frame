using CallCenter.Core.DataPlane;
using CallCenter.Core.Discovery;
using CallCenter.Core.Router;
using CallCenter.Core.Tools;
using CallCenter.Core.Tracks;
using CallCenter.Mcp;
using CSG.Unified;

namespace CallCenter.Tests;

/// <summary>
/// P3 首类轨整合：C# 轨与 MCPBridge(TS/PS/ExternalMCP) 轨并入同一 <see cref="ToolRouter"/> 与
/// <see cref="CsgToolCatalog"/>，构成统一工具面 —— 同名工具先登记轨生效（C# 优先），
/// 仅某轨存在的工具按名路由到对应轨；Catalog 投影同时含 C#/TypeScript/PowerShell/ExternalMCP 四类 backend。
/// </summary>
[TestClass]
public class UnifiedToolFaceTests
{
    [TestMethod]
    public async Task Router_Holds_Csharp_And_McpBridge_Tracks_As_One_Tool_Face()
    {
        var router = BuildUnifiedRouter();

        // 统一表：同名 sample_add 仅一份且是 C#；其余各轨专属工具按各自 backend 保留。
        var names = router.Tools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.AreEqual(1, router.Tools.Count(t => t.Name == "sample_add"));
        Assert.AreEqual(ToolBackend.CSharp, router.Tools.First(t => t.Name == "sample_add").Backend);
        Assert.IsTrue(names.Contains("sample_echo"));
        Assert.IsTrue(names.Contains("node_select"));
        Assert.IsTrue(names.Contains(BridgeLocalToolClassifier.PowerShellToolName));
        Assert.IsTrue(names.Contains("remote_tool"));

        // 按名跨轨路由：sample_add → C#(a+b=5)；node_select → MCPBridge(TS)；invoke_ps_script → MCPBridge(PS)。
        var csharp = await router.CallAsync("sample_add", "{\"a\":2,\"b\":3}");
        Assert.IsTrue(csharp.Success);
        Assert.AreEqual("5", csharp.OutputJson);

        var ts = await router.CallAsync("node_select", "{\"q\":\"x\"}");
        Assert.IsTrue(ts.Success);
        Assert.AreEqual("mcp:node_select", ts.OutputJson);

        var ps = await router.CallAsync(BridgeLocalToolClassifier.PowerShellToolName, null);
        Assert.IsTrue(ps.Success);
        Assert.AreEqual("mcp:invoke_ps_script", ps.OutputJson);

        var ext = await router.CallAsync("remote_tool", null);
        Assert.IsTrue(ext.Success);
        Assert.AreEqual("mcp:remote_tool", ext.OutputJson);
    }

    [TestMethod]
    public void Catalog_Projects_All_Four_Backends()
    {
        var router = BuildUnifiedRouter();
        var catalog = new CsgToolCatalog(new CsgFacade());
        catalog.RegisterRange(router.Tools);

        var backends = catalog.Tools.Select(t => t.Backend).ToHashSet(StringComparer.OrdinalIgnoreCase);
        CollectionAssert.AreEquivalent(
            new[] { ToolBackend.CSharp, ToolBackend.TypeScript, ToolBackend.PowerShell, ToolBackend.ExternalMCP },
            backends.ToArray());
    }

    private static ToolRouter BuildUnifiedRouter()
    {
        var router = new ToolRouter();

        // C# 轨：SampleHostLibrary（sample_add / sample_echo）。
        var csharpDefs = CSharpFunctionLibrarySource
            .FromLibraryTypes(new[] { typeof(CSharpFunctionLibrarySourceTests.SampleHostLibrary) })
            .DiscoverDefinitions();
        router.AddTrack(new CSharpTrack(csharpDefs));

        // MCPBridge 轨：内存客户端（不拉起 node）。自定义分类器把 sample_add/node_select 归 TS。
        var client = new FakeBridgeClient();
        var classifier = new BridgeLocalToolClassifier(typeScriptTools: new[] { "sample_add", "node_select" });
        router.AddTrack(new McpBridgeTrack(
            client,
            client.Tools.Select(t => McpToolMapper.ToDescriptor(t, classifier.Classify(t.Name))).ToArray()));

        return router;
    }

    private sealed class FakeBridgeClient : IMcpToolClient
    {
        public McpToolInfo[] Tools { get; } =
        {
            new("sample_add", "TS 同名工具（应被 C# 先登记轨屏蔽）", default),
            new("node_select", "选择 Node 上下文", default),
            new(BridgeLocalToolClassifier.PowerShellToolName, "执行 PowerShell 脚本", default),
            new("remote_tool", "聚合的远程服务器工具", default),
        };

        public Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(bool forceRefresh = false, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<McpToolInfo>>(Tools);

        public Task<McpCallResponse> CallToolAsync(string toolName, string? argumentsJson, CancellationToken ct = default)
            => Task.FromResult(McpCallResponse.Ok($"mcp:{toolName}"));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
