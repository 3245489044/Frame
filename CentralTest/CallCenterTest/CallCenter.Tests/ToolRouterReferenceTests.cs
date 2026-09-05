using CallCenter.Core.Router;
using CallCenter.Core.Tracks;
using CallCenter.Core.Tools;

namespace CallCenter.Tests;

/// <summary>
/// P4-a 原子换 router 快照的线程安全持有器：热换以「构建全新 ToolRouter → Replace 换引用」承载，
/// Current 每次拿到完整一致快照，不原位改共享字典（消除并发热换与读取竞态）。本测试验证换引用语义与一致快照读取。
/// </summary>
[TestClass]
public class ToolRouterReferenceTests
{
    [TestMethod]
    public void Replace_Swaps_Current_To_New_Snapshot()
    {
        var r1 = BuildRouter("tool_a");
        var reference = new ToolRouterReference(r1);

        Assert.AreSame(r1, reference.Current);

        var r2 = BuildRouter("tool_b");
        reference.Replace(r2);

        Assert.AreSame(r2, reference.Current, "Replace 后 Current 应指向新路由快照。");
        Assert.IsTrue(reference.Current.CanCall("tool_b"));
        Assert.IsFalse(reference.Current.CanCall("tool_a"));
    }

    [TestMethod]
    public void Replace_Null_Throws()
    {
        var reference = new ToolRouterReference(BuildRouter("tool_a"));
        Assert.ThrowsExactly<ArgumentNullException>(() => reference.Replace(null!));
    }

    /// <summary>建一个含单条假工具轨的 router（无真实委托，只验证工具面名称/CanCall 索引）。</summary>
    private static ToolRouter BuildRouter(string name)
    {
        var router = new ToolRouter();
        router.AddTrack(new StubTrack(name));
        return router;
    }

    private sealed class StubTrack : ICallableTrack
    {
        private readonly ToolDescriptor _tool;

        public StubTrack(string name)
            => _tool = new ToolDescriptor($"tool:stub:{name}", name, ToolBackend.CSharp, 1, name, null, null, ToolFlags.Deterministic);

        public string Backend => ToolBackend.CSharp;

        public IReadOnlyCollection<ToolDescriptor> Tools => new[] { _tool };

        public bool CanCall(string name) => string.Equals(name, _tool.Name, StringComparison.OrdinalIgnoreCase);

        public Task<ToolExecutionResult> CallAsync(string name, string? argumentsJson, CancellationToken ct = default)
            => Task.FromResult(ToolExecutionResult.Fail("stub")); // 只测工具面/CanCall，不真执行。
    }
}
