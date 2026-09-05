using CallCenter.Core.Discovery;
using CallCenter.Core.Router;
using CallCenter.Core.Tools;
using CallCenter.Core.Tracks;
using TConfig;

namespace CallCenter.Tests;

/// <summary>锁定 ToolRouter（控制面）：Lookup(name/id/csg 寻址)、多轨路由、同名冲突先登记轨生效。</summary>
[TestClass]
public class ToolRouterTests
{
    private static CSharpTrack BuildCSharpTrack()
        => new(CSharpFunctionLibrarySource.FromLibraryTypes(new[] { typeof(CSharpFunctionLibrarySourceTests.SampleHostLibrary) }).DiscoverDefinitions());

    private static ToolDescriptor SimpleTool(string name, string backend)
        => new($"tool:{backend.ToLowerInvariant()}:{name}", name, backend, 1, "fake",
            new Dictionary<string, object?>(), new Dictionary<string, object?>(),
            ToolFlags.Externalized);

    private static ToolRouter BuildRouter()
    {
        var router = new ToolRouter();
        router.AddTrack(BuildCSharpTrack());              // C# 轨（sample_add / sample_echo）
        router.AddTrack(new FakeTrack(SimpleTool("sample_add", "TypeScript"))); // 同名另一轨 → 应被先登记轨屏蔽
        router.AddTrack(new FakeTrack(SimpleTool("greet", "TypeScript")));      // 仅 TS 轨存在
        return router;
    }

    [TestMethod]
    public void TryResolve_By_Name_And_By_Csg_Address()
    {
        var router = BuildRouter();

        Assert.IsTrue(router.TryResolve("sample_add", out var byName));
        Assert.IsTrue(router.TryResolve("tool:csharp:sample_add", out var byId));
        Assert.IsTrue(router.TryResolve("csg://tool/sample_add", out var byCsg));
        Assert.IsFalse(router.TryResolve("missing", out _));
        Assert.AreEqual(byName.Id, byId.Id);
        Assert.AreEqual(byName.Id, byCsg.Id);
    }

    [TestMethod]
    public async Task Call_Routes_To_Track_That_Can_Call()
    {
        var router = BuildRouter();

        var csharp = await router.CallAsync("sample_add", "{\"a\":2,\"b\":3}");
        Assert.IsTrue(csharp.Success);
        Assert.AreEqual("5", csharp.OutputJson, "sample_add 应路由到 C#Track 真执行");

        var tsOnly = await router.CallAsync("greet", null);
        Assert.IsTrue(tsOnly.Success);
        Assert.AreEqual("\"fake-ts\"", tsOnly.OutputJson, "greet 仅 TS 轨可调 → 路由到 FakeTrack");
    }

    [TestMethod]
    public void Same_Name_First_Registered_Track_Wins()
    {
        // sample_add 同时存在于 C# 与 TS 两轨 → Router 全表仅一份描述符，先登记轨（C#）生效。
        var router = BuildRouter();
        var tools = router.Tools;
        Assert.AreEqual(1, tools.Count(t => t.Name == "sample_add"));
        Assert.AreEqual(ToolBackend.CSharp, tools.First(t => t.Name == "sample_add").Backend);
    }

    [TestMethod]
    public async Task Unknown_Tool_Returns_Failure()
    {
        var router = BuildRouter();
        var result = await router.CallAsync("no_such_tool", null);
        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "无可执行工具");
    }

    [TestMethod]
    public async Task ReplaceTrack_Switches_Active_Implementation_For_Same_Backend()
    {
        // P4：同 backend 新轨替换旧轨 → 同名工具 sample_add 活性实现原子切到新版本（a+b → a*b）。
        var router = new ToolRouter();
        router.AddTrack(BuildTrack(typeof(ReloadOldLibrary)));   // sample_add = a+b → 2,3 = 5
        var before = await router.CallAsync("sample_add", "{\"a\":2,\"b\":3}");
        Assert.IsTrue(before.Success);
        Assert.AreEqual("5", before.OutputJson);

        router.ReplaceTrack(BuildTrack(typeof(ReloadNewLibrary))); // sample_add = a*b → 2,3 = 6

        var after = await router.CallAsync("sample_add", "{\"a\":2,\"b\":3}");
        Assert.IsTrue(after.Success);
        Assert.AreEqual("6", after.OutputJson, "替换后应按新轨实现执行（版本活性切换）");
        Assert.AreEqual(1, router.Tools.Count(t => t.Name == "sample_add"), "全表同名工具仍仅一份");
    }

    [TestMethod]
    public async Task ReplaceTrack_Leaves_Other_Backend_Tracks_Intact()
    {
        var router = new ToolRouter();
        router.AddTrack(BuildTrack(typeof(CSharpFunctionLibrarySourceTests.SampleHostLibrary))); // C#: sample_add/sample_echo
        router.AddTrack(new FakeTrack(SimpleTool("greet", "TypeScript")));

        router.ReplaceTrack(BuildTrack(typeof(ReloadNewLibrary))); // 仅替换 C# backend

        Assert.IsTrue(router.Tools.Any(t => t.Name == "greet" && t.Backend == "TypeScript"), "非替换 backend 的轨应保留");
        Assert.IsFalse(router.Tools.Any(t => t.Name == "sample_echo"), "被替换的 C# 旧轨应移除，其专属工具不再暴露");
        Assert.IsTrue(router.Tools.Any(t => t.Name == "sample_add" && t.Backend == "CSharp"));

        var greet = await router.CallAsync("greet", null);
        Assert.IsTrue(greet.Success);
        Assert.AreEqual("\"fake-ts\"", greet.OutputJson);
    }

    private static CSharpTrack BuildTrack(Type libraryType)
        => new(CSharpFunctionLibrarySource.FromLibraryTypes(new[] { libraryType }).DiscoverDefinitions());

    /// <summary>P4 版本活性测试库：sample_add = a + b（v1）。</summary>
    public static class ReloadOldLibrary
    {
        public static void RegisterAll(ITrConfigRegistry registry)
            => registry.Register("sample_add", (Func<double, double, double>)((a, b) => a + b), "sample_add v1: a+b");
    }

    /// <summary>P4 版本活性测试库：sample_add = a * b（v2，替换后应生效）。</summary>
    public static class ReloadNewLibrary
    {
        public static void RegisterAll(ITrConfigRegistry registry)
            => registry.Register("sample_add", (Func<double, double, double>)((a, b) => a * b), "sample_add v2: a*b");
    }

    private sealed class FakeTrack : ICallableTrack
    {
        private readonly ToolDescriptor _tool;

        public FakeTrack(ToolDescriptor tool) => _tool = tool;

        public string Backend => _tool.Backend;

        public IReadOnlyCollection<ToolDescriptor> Tools => new[] { _tool };

        public bool CanCall(string name) => string.Equals(name, _tool.Name, StringComparison.OrdinalIgnoreCase);

        public Task<ToolExecutionResult> CallAsync(string name, string? argumentsJson, CancellationToken ct = default)
            => Task.FromResult(ToolExecutionResult.Ok("\"fake-ts\""));
    }
}
