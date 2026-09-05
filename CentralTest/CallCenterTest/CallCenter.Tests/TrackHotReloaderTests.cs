using CallCenter.Core.Cache;
using CallCenter.Core.Discovery;
using CallCenter.Core.Router;
using CallCenter.Core.Tracks;
using CallCenter.Core.Tools;
using TConfig;

namespace CallCenter.Tests;

/// <summary>
/// P4-a 热重载编排协调器：内容指纹变 → 换轨 + 登记新版本 + 回收旧版；指纹未变 → 跳过；
/// 旧版有在途调用则保留、归零后才回收（新版本激活 → 旧在途不破坏 → 归零回收 的无损切换）。
/// 版本/引用状态机语义见 <see cref="HotReloadVersionStore"/>；本测试验证协调器把 ReplaceTrack 与其串成闭环。
/// </summary>
[TestClass]
public class TrackHotReloaderTests
{
    private static CSharpTrack BuildTrack(Type libraryType)
        => new(CSharpFunctionLibrarySource.FromLibraryTypes(new[] { libraryType }).DiscoverDefinitions());

    [TestMethod]
    public async Task Reload_With_New_Fingerprint_Swaps_Active_Implementation()
    {
        var router = new ToolRouter();
        var reloader = new TrackHotReloader(router);

        // v1: sample_add = a+b → 5
        var r1 = reloader.Reload(ToolBackend.CSharp, "fp-1", () => BuildTrack(typeof(ToolRouterTests.ReloadOldLibrary)));
        Assert.IsTrue(r1.Swapped);
        Assert.AreEqual("5", (await router.CallAsync("sample_add", "{\"a\":2,\"b\":3}")).OutputJson);

        // v2: sample_add = a*b → 6
        var r2 = reloader.Reload(ToolBackend.CSharp, "fp-2", () => BuildTrack(typeof(ToolRouterTests.ReloadNewLibrary)));
        Assert.IsTrue(r2.Swapped);
        Assert.AreEqual("6", (await router.CallAsync("sample_add", "{\"a\":2,\"b\":3}")).OutputJson);
        Assert.AreEqual("fp-2#sample_add", reloader.Versions.ActiveFingerprint("sample_add"));
    }

    [TestMethod]
    public void Reload_With_Same_Fingerprint_Is_Noop()
    {
        var router = new ToolRouter();
        var reloader = new TrackHotReloader(router);

        reloader.Reload(ToolBackend.CSharp, "fp-1", () => BuildTrack(typeof(ToolRouterTests.ReloadOldLibrary)));
        var again = reloader.Reload(ToolBackend.CSharp, "fp-1", () => BuildTrack(typeof(ToolRouterTests.ReloadNewLibrary)));

        Assert.IsFalse(again.Swapped, "内容指纹未变 → 不得换轨（即使提供了不同实现）");
    }

    [TestMethod]
    public async Task Reload_Keeps_Old_InFlight_Until_Zero_Then_Prunes()
    {
        var router = new ToolRouter();
        var reloader = new TrackHotReloader(router);

        // v1 激活
        reloader.Reload(ToolBackend.CSharp, "fp-1", () => BuildTrack(typeof(ToolRouterTests.ReloadOldLibrary)));
        const string tool = "sample_add";
        const string fp1 = "fp-1#sample_add";
        Assert.AreEqual(fp1, reloader.Versions.ActiveFingerprint(tool));

        // v1 在途调用开始
        reloader.Versions.BeginInFlight(tool, fp1);

        // v2 热切换
        reloader.Reload(ToolBackend.CSharp, "fp-2", () => BuildTrack(typeof(ToolRouterTests.ReloadNewLibrary)));

        Assert.IsFalse(reloader.Versions.IsDrainable(tool, fp1), "旧版 v1 有在途调用 → 不可回收");
        Assert.IsTrue(reloader.Versions.Versions(tool).Contains(fp1), "旧版档案保留，不破坏在途调用");

        // 在途归零 → 可回收
        reloader.Versions.EndInFlight(tool, fp1);
        Assert.IsTrue(reloader.Versions.IsDrainable(tool, fp1));

        var pruned = reloader.Versions.PruneDrained(tool);
        Assert.AreEqual(1, pruned);
        Assert.IsFalse(reloader.Versions.Versions(tool).Contains(fp1), "归零后回收旧版档案");
        Assert.AreEqual("fp-2#sample_add", reloader.Versions.ActiveFingerprint(tool));

        // 路由活性仍是 v2（a*b）
        var result = await router.CallAsync(tool, "{\"a\":2,\"b\":3}");
        Assert.IsTrue(result.Success);
        Assert.AreEqual("6", result.OutputJson);
    }

    [TestMethod]
    public void Reload_Other_Backend_Leaves_CSharp_Track_Intact()
    {
        var router = new ToolRouter();
        router.AddTrack(BuildTrack(typeof(ToolRouterTests.ReloadOldLibrary))); // C# 轨
        var reloader = new TrackHotReloader(router);

        // 对另一 backend（TypeScript）热重载 → 不应触碰 C# 轨。
        reloader.Reload(ToolBackend.TypeScript, "fp-ts", () => new FakeTrack());

        Assert.IsTrue(router.Tools.Any(t => t.Name == "sample_add" && t.Backend == ToolBackend.CSharp));
        Assert.IsFalse(router.Tools.Any(t => t.Name == "ts_only"));
    }

    [TestMethod]
    public void Reload_Removing_Tools_Retires_Their_Version_Entries()
    {
        var router = new ToolRouter();
        var reloader = new TrackHotReloader(router);

        // v1：sample_add + sample_echo
        var r1 = reloader.Reload(ToolBackend.CSharp, "fp-1", () => BuildTrack(typeof(ShrinkV1Library)));
        Assert.IsTrue(r1.Swapped);
        Assert.AreEqual(2, r1.ToolCount);
        Assert.AreEqual(1, reloader.Versions.Versions("sample_echo").Count);

        // v2：仅 sample_add —— sample_echo 应从版本表整名退役（B2/C3），不再残留为 Active。
        var r2 = reloader.Reload(ToolBackend.CSharp, "fp-2", () => BuildTrack(typeof(ShrinkV2Library)));
        Assert.IsTrue(r2.Swapped);
        Assert.AreEqual(0, reloader.Versions.Versions("sample_echo").Count, "换轨移除的工具 sample_echo 应整名退役（B2/C3）");
        Assert.IsNull(reloader.Versions.ActiveFingerprint("sample_echo"));
        Assert.AreEqual(1, reloader.Versions.Versions("sample_add").Count);
    }

    /// <summary>两工具库：sample_add + sample_echo（v1，用于换轨移除验证）。</summary>
    public static class ShrinkV1Library
    {
        public static void RegisterAll(ITrConfigRegistry registry)
        {
            registry.Register("sample_add", (Func<double, double, double>)((a, b) => a + b), "v1 add");
            registry.Register("sample_echo", (Func<string, string>)(s => s), "v1 echo");
        }
    }

    /// <summary>单工具库：仅 sample_add（v2，移除 sample_echo）。</summary>
    public static class ShrinkV2Library
    {
        public static void RegisterAll(ITrConfigRegistry registry)
            => registry.Register("sample_add", (Func<double, double, double>)((a, b) => a * b), "v2 add");
    }

    private sealed class FakeTrack : ICallableTrack
    {
        public string Backend => ToolBackend.TypeScript;

        public IReadOnlyCollection<ToolDescriptor> Tools => Array.Empty<ToolDescriptor>();

        public bool CanCall(string name) => false;

        public Task<ToolExecutionResult> CallAsync(string name, string? argumentsJson, CancellationToken ct = default)
            => Task.FromResult(ToolExecutionResult.Fail("no-op"));
    }
}
