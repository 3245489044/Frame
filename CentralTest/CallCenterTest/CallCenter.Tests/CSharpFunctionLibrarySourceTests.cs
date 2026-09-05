using CallCenter.Core.Discovery;
using CallCenter.Core.Tools;
using TConfig;

namespace CallCenter.Tests;

/// <summary>
/// 验证「真源替换」：CSharpFunctionLibrarySource 通过反射执行真实 C# 函数库的 RegisterAll(ITrConfigRegistry)，
/// 把注册函数投影为 ToolDescriptor，而非硬编码种子。测试库遵循仓库跨程序集约定（无 [FunctionLibrary] 依赖亦可）。
/// </summary>
[TestClass]
public class CSharpFunctionLibrarySourceTests
{
    [TestMethod]
    public void Discover_Executes_RegisterAll_And_Projects_Tools()
    {
        var source = CSharpFunctionLibrarySource.FromLibraryTypes(new[] { typeof(SampleHostLibrary) });

        var tools = source.Discover();

        Assert.AreEqual(2, tools.Count, "样例库注册了 2 个函数");
        var echo = tools.Single(t => t.Name == "sample_echo");
        Assert.AreEqual(ToolBackend.CSharp, echo.Backend);
        Assert.IsTrue(echo.Flags.HasFlag(ToolFlags.Deterministic));
        Assert.IsTrue(echo.Flags.HasFlag(ToolFlags.HotReloadable));
        Assert.IsTrue(echo.Id.StartsWith("tool:csharp:", StringComparison.Ordinal));
        Assert.IsFalse(string.IsNullOrWhiteSpace(echo.Fingerprint));
        // 输入 schema 由委托参数投影（非空，说明真实 schema 已进入 metadata）
        Assert.IsTrue(echo.InputSchema.ContainsKey("text"));
    }

    [TestMethod]
    public void Discover_Is_Stable_Across_Runs()
    {
        var source = CSharpFunctionLibrarySource.FromLibraryTypes(new[] { typeof(SampleHostLibrary) });
        var a = source.Discover();
        var b = source.Discover();

        Assert.AreEqual(a.Count, b.Count);
        for (var i = 0; i < a.Count; i++)
            Assert.AreEqual(a[i].Fingerprint, b[i].Fingerprint);
    }

    [TestMethod]
    public void Duplicate_Function_Name_Across_Libraries_Throws()
    {
        // 两个库注册同名函数 → 收集器同名冲突（对齐 FunctionLibraryLoader 同名唯一规则）。
        var source = CSharpFunctionLibrarySource.FromLibraryTypes(new[] { typeof(SampleHostLibrary), typeof(ConflictingDuplicateLibrary) });

        ThrowsAssert.Throws<InvalidOperationException>(() => source.Discover());
    }

    [TestMethod]
    public void Missing_RegisterAll_Throws()
    {
        var source = CSharpFunctionLibrarySource.FromLibraryTypes(new[] { typeof(NotALibrary) });
        ThrowsAssert.Throws<InvalidOperationException>(() => source.Discover());
    }

    // ---- 测试夹具（真实 C# 函数库，遵循 RegisterAll(ITrConfigRegistry) 约定） ----

    public static class SampleHostLibrary
    {
        public static void RegisterAll(ITrConfigRegistry registry)
        {
            registry.Register("sample_echo", (string text) => text, "回显（样例）");
            registry.Register("sample_add", (double a, double b) => a + b, "相加（样例）");
        }
    }

    public static class ConflictingDuplicateLibrary
    {
        public static void RegisterAll(ITrConfigRegistry registry)
            => registry.Register("sample_echo", (string text) => text, "冲突同名");
    }

    public static class NotALibrary
    {
        // 无 RegisterAll(ITrConfigRegistry)：应被发现源拒绝。
        public static string JustAHelper() => "hi";
    }
}
