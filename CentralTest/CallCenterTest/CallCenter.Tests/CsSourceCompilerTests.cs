using CallCenter.Adapters.Compilation;
using CallCenter.Core.Cache;

namespace CallCenter.Tests;

/// <summary>
/// P4-b C# 编译轨：经 RoslynMethod.Core（复用仓库既有 Roslyn 核心，非自造编译栈）把 C# 源编译为 IL 字节，
/// 并按 fingerprint 写入 <see cref="BuildCache"/>（编译缓存多轨制）。本测试锁「源码→IL + BuildCache 写读」的 C# 腿；
/// 激活（产物自动入 Router ReplaceTrack）属持久宿主 E2E 另一腿（watch），不在本测试范围。
/// </summary>
[TestClass]
public class CsSourceCompilerTests
{
    private static string CsDir => Path.Combine(Path.GetTempPath(), "callcenter_cstest_" + Guid.NewGuid().ToString("N"));

    [TestMethod]
    public async Task Compile_Produces_PE_And_Loads_Assembly()
    {
        var compiler = new CsSourceCompiler();

        // 简单确定性工具源码：返回两数之和的静态方法（镜像 CallCenterHostLibrary.cc_add 形态）。
        const string source = """
            using System;
            namespace CsTool;
            public static class Tool
            {
                public static double Add(double a, double b) => a + b;
            }
            """;

        var il = await compiler.CompileAsync(source);

        Assert.IsNotNull(il);
        Assert.IsTrue(il.Length > 0, "Roslyn 应产出非空 IL 字节。");
        // PE 头标识（MZ），确为可加载程序集产物。
        Assert.AreEqual((byte)'M', il[0]);
        Assert.AreEqual((byte)'Z', il[1]);

        // 产物可 Assembly.Load 并命中预期方法（证明产物是真 IL，非空壳）。
        var asm = System.Reflection.Assembly.Load(il);
        var type = asm.GetType("CsTool.Tool", throwOnError: true)!;
        var method = type.GetMethod("Add")!;
        var result = method.Invoke(null, new object[] { 40.0, 2.0 });
        Assert.AreEqual(42.0, result);
    }

    [TestMethod]
    public async Task Compile_Writes_Into_BuildCache_By_Fingerprint()
    {
        var dir = CsDir;
        Directory.CreateDirectory(dir);
        try
        {
            var cs = Path.Combine(dir, "tool.cs");
            const string source = """
                namespace CsTool2;
                public static class Tool
                {
                    public static string Greet(string name) => "hi " + name;
                }
                """;
            await File.WriteAllTextAsync(cs, source);

            var compiler = new CsSourceCompiler();
            var il = await compiler.CompileFileAsync(cs);

            // 编译产物写 BuildCache（key=fingerprint，ContentHash 单一实现 / A5）：命中即不再走编译。
            var cache = new BuildCache();
            var fp = ContentHash.Compute(cs);
            cache.Put(fp, il);
            Assert.IsTrue(cache.TryGet(fp, out var artifact));
            Assert.AreEqual(il, artifact);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task Compile_InvalidSource_Throws_With_Diagnostics()
    {
        var compiler = new CsSourceCompiler();

        const string bad = "namespace CsBad; public class Broken { public void X( }"; // 语法错误

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => compiler.CompileAsync(bad));
        StringAssert.Contains(ex.Message, "编译失败");
    }
}
