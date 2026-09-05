using CallCenter.Core.Cache;
using CallCenter.Mcp;

namespace CallCenter.Tests;

/// <summary>
/// P4-b TS 编译轨：经 workspace tsc（node 执行 JS）把 TS 源编译为 CommonJS 产物，并按 fingerprint 写入
/// <see cref="BuildCache"/>（编译缓存多轨制）。tsc 未就绪时测试 Inconclusive（不阻塞全绿）。
/// 注：C# 源码→IL 走 Roslyn 与「编译产物→持久宿主自动激活」属另一腿/常驻宿主 E2E，本测试只锁 TS 腿与 BuildCache 写读。
/// </summary>
[TestClass]
public class TsScriptCompilerTests
{
    private static string TsDir => Path.Combine(Path.GetTempPath(), "callcenter_tstest_" + Guid.NewGuid().ToString("N"));

    [TestMethod]
    public async Task Compile_Writes_Js_And_Registers_Into_BuildCache()
    {
        if (TsScriptCompiler.ResolveTypescriptBin() is null)
            Assert.Inconclusive("workspace typescript/bin/tsc 未就绪，跳过 tsc 编译测试。");

        var dir = TsDir;
        Directory.CreateDirectory(dir);
        try
        {
            var ts = Path.Combine(dir, "tool.ts");
            await File.WriteAllTextAsync(ts, "export function greet(name: string): string { return \"hi \" + name; }\n");

            var compiler = new TsScriptCompiler();
            var outDir = Path.Combine(dir, "out");
            var js = await compiler.CompileAsync(ts, outDir);

            Assert.IsTrue(File.Exists(js), "tsc 应产出 .js");
            var jsText = await File.ReadAllTextAsync(js);
            StringAssert.Contains(jsText, "greet");

            // 编译产物写 BuildCache（key=fingerprint）：命中即不再走编译。指纹单一实现 = Core ContentHash（A5）。
            var cache = new BuildCache();
            var fp = ContentHash.Compute(ts);
            cache.Put(fp, js);
            Assert.IsTrue(cache.TryGet(fp, out var artifact));
            Assert.AreEqual(js, artifact);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
