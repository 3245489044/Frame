using Agent.Contract.Llm;
using Agent.Modules;
using CallCenter.Core.Router;
using CallCenter.Core.Tools;
using CallCenter.Core.Tracks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Agent.McpRegistryHost.Tests;

/// <summary>
/// P4-a 折叠前置工具面「动态取当前 router」：<see cref="CallCenterFoldToolProvider"/> 经
/// <see cref="ToolRouterReference"/> 每次取当前路由快照 —— 常驻宿主热换（原子 Replace 换全新 router）后，
/// 下一次 GetToolsAsync/CallAsync 立即反映新版本，且不触碰旧快照/不原位改共享结构（消除并发热换竞态）。
/// </summary>
[TestClass]
public class CallCenterFoldToolProviderTests
{
    [TestMethod]
    public async Task GetTools_Reflects_Current_Router_After_HotSwap()
    {
        var reference = new ToolRouterReference(BuildRouter("v1_tool", "v1"));
        var provider = new CallCenterFoldToolProvider(reference);

        var before = await provider.GetToolsAsync(CancellationToken.None);
        CollectionAssert.AreEquivalent(new[] { "v1_tool" }, before.Select(t => t.Name).ToArray());

        // 热换：构建全新 router 快照并原子替换引用（不 ReplaceTrack 原位改同一 router）。
        reference.Replace(BuildRouter("v2_tool", "v2"));
        var after = await provider.GetToolsAsync(CancellationToken.None);

        CollectionAssert.AreEquivalent(new[] { "v2_tool" }, after.Select(t => t.Name).ToArray(),
            "热换后 GetTools 应反映新快照，而非旧 router 残留工具。");
    }

    [TestMethod]
    public async Task CallAsync_Routes_To_Current_Router_Implementation()
    {
        var reference = new ToolRouterReference(BuildRouter("tool_a", "v1"));
        var provider = new CallCenterFoldToolProvider(reference);

        // 第一版：tool_a → 返回 "v1:tool_a"，且此时无 tool_b。
        var r1 = await provider.CallAsync("tool_a", null);
        StringAssert.Contains(r1, "v1:tool_a");

        // 热换为 tool_b（返回 v2:tool_b），tool_a 从快照移除。
        reference.Replace(BuildRouter("tool_b", "v2"));
        var r2 = await provider.CallAsync("tool_b", null);
        StringAssert.Contains(r2, "v2:tool_b");

        // 旧工具在新快照下不可达 → provider 返回 CallCenter 无可执行工具的 error（externalFallback 为 null）。
        var r3 = await provider.CallAsync("tool_a", null);
        StringAssert.Contains(r3, "error");
    }

    /// <summary>建一个含单条可执行 stub 轨的 router（真实委托返回 {prefix}:{name}，验证 CallAsync 路由到当前实现版本）。</summary>
    private static ToolRouter BuildRouter(string name, string prefix)
    {
        var router = new ToolRouter();
        router.AddTrack(new StubExecutableTrack(name, prefix));
        return router;
    }

    private sealed class StubExecutableTrack : ICallableTrack
    {
        private readonly string _name;
        private readonly string _prefix;

        public StubExecutableTrack(string name, string prefix)
        {
            _name = name;
            _prefix = prefix;
        }

        public string Backend => ToolBackend.CSharp;

        public IReadOnlyCollection<ToolDescriptor> Tools => new[]
        {
            new ToolDescriptor($"tool:csharp:{_name}", _name, ToolBackend.CSharp, 1, _name, null, null, ToolFlags.Deterministic),
        };

        public bool CanCall(string name) => string.Equals(name, _name, StringComparison.OrdinalIgnoreCase);

        public Task<ToolExecutionResult> CallAsync(string name, string? argumentsJson, CancellationToken ct = default)
            => Task.FromResult(ToolExecutionResult.Ok($"{_prefix}:{name}"));
    }
}
