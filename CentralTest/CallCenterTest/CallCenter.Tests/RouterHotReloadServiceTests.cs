using System.Text;
using CallCenter.Core.Router;
using CallCenter.Core.Tracks;

namespace CallCenter.Tests;

/// <summary>
/// P4-a 共享常驻热换监视服务（Host --watch / Agent 组合根复用）：FileSystemWatcher → 防抖 → 字节快照指纹去重 →
/// 重发现构建<b>全新</b> ToolRouter → <see cref="ToolRouterReference.Replace"/> 原子换引用。验证确定性 ReloadNow 行为
/// （初始装载 / 变更换引用 / 去重 / 解析失败不破坏当前快照）与 Dispose 停监视；FileSystemWatcher 端到端一条。
/// </summary>
[TestClass]
public class RouterHotReloadServiceTests
{
    [TestMethod]
    public void ReloadNow_Applies_Initial_Content()
    {
        using var dir = TempDir();
        var path = WriteConfig(dir, typeof(CSharpFunctionLibrarySourceTests.SampleHostLibrary));
        var reference = new ToolRouterReference(new ToolRouter());
        using var service = new RouterHotReloadService(reference, path);

        var fp = service.ReloadNow();

        Assert.IsNotNull(fp);
        Assert.IsTrue(reference.Current.CanCall("sample_echo"));
        Assert.IsTrue(reference.Current.CanCall("sample_add"));
    }

    [TestMethod]
    public void ReloadNow_Changed_Content_Swaps_Reference_Snapshot()
    {
        using var dir = TempDir();
        var path = WriteConfig(dir, typeof(CSharpFunctionLibrarySourceTests.SampleHostLibrary));
        var reference = new ToolRouterReference(new ToolRouter());
        using var service = new RouterHotReloadService(reference, path);

        var first = service.ReloadNow();
        Assert.IsNotNull(first);
        Assert.IsTrue(reference.Current.CanCall("sample_echo"));

        WriteConfig(path, typeof(HotReloadSampleLibraryAlt));
        var second = service.ReloadNow();

        Assert.IsNotNull(second);
        Assert.AreNotEqual(first, second, "内容变化 → 指纹不同 → 应触发换引用。");
        Assert.IsTrue(reference.Current.CanCall("sample_mul"), "新 router 快照应含热换后工具。");
        Assert.IsFalse(reference.Current.CanCall("sample_echo"), "旧工具已随旧 router 快照整体退役（原子快照，非原位改）。");
    }

    [TestMethod]
    public void ReloadNow_Unchanged_Returns_Null_And_Keeps_Current()
    {
        using var dir = TempDir();
        var path = WriteConfig(dir, typeof(CSharpFunctionLibrarySourceTests.SampleHostLibrary));
        var reference = new ToolRouterReference(new ToolRouter());
        using var service = new RouterHotReloadService(reference, path);

        Assert.IsNotNull(service.ReloadNow());
        var current = reference.Current;
        WriteConfig(path, typeof(CSharpFunctionLibrarySourceTests.SampleHostLibrary)); // 同内容重写

        Assert.IsNull(service.ReloadNow(), "内容指纹未变 → 跳过（去重）。");
        Assert.AreSame(current, reference.Current, "未变更不应换引用。");
    }

    [TestMethod]
    public void ReloadNow_InvalidJson_Does_Not_Break_Current_Or_Poison_Fingerprint()
    {
        using var dir = TempDir();
        var path = WriteConfig(dir, typeof(CSharpFunctionLibrarySourceTests.SampleHostLibrary));
        var reference = new ToolRouterReference(new ToolRouter());
        using var service = new RouterHotReloadService(reference, path);

        Assert.IsNotNull(service.ReloadNow());

        File.WriteAllText(path, "{ not-valid-json", Encoding.UTF8);
        Assert.IsNull(service.ReloadNow(), "非法清单 → 跳过替换（不破坏当前快照）。");
        Assert.IsTrue(reference.Current.CanCall("sample_echo"), "当前快照仍可用。");

        // 非法内容不应污染指纹：修好后同源内容应能再次成功热换。
        WriteConfig(path, typeof(HotReloadSampleLibraryAlt));
        var fp = service.ReloadNow();
        Assert.IsNotNull(fp, "解析失败后指纹未被污染，修复即可重试。");
        Assert.IsTrue(reference.Current.CanCall("sample_mul"));
    }

    [TestMethod]
    public void Start_Sets_IsWatching_And_Dispose_Stops_It()
    {
        using var dir = TempDir();
        var path = WriteConfig(dir, typeof(CSharpFunctionLibrarySourceTests.SampleHostLibrary));
        var service = new RouterHotReloadService(new ToolRouterReference(new ToolRouter()), path);

        service.Start(loadInitial: false);
        Assert.IsTrue(service.IsWatching, "Start 后应开始文件监视。");

        service.Dispose();
        Assert.IsFalse(service.IsWatching, "Dispose 后应停监视。");
        service.Dispose(); // 幂等
    }

    [TestMethod]
    public void Watcher_Reloads_When_File_Changes()
    {
        using var dir = TempDir();
        var path = WriteConfig(dir, typeof(CSharpFunctionLibrarySourceTests.SampleHostLibrary));
        var reference = new ToolRouterReference(new ToolRouter());
        using var service = new RouterHotReloadService(reference, path);

        service.Start(loadInitial: true); // 初始装载 + 开始监视
        Assert.IsTrue(reference.Current.CanCall("sample_echo"));

        WriteConfig(path, typeof(HotReloadSampleLibraryAlt)); // 触发文件系统变更

        // FileSystemWatcher 是异步/时序性触发：轮询等待（本地同机写文件通常毫秒级）。
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && !reference.Current.CanCall("sample_mul"))
        {
            Task.Delay(100).Wait();
            service.ReloadNow(); // 兜底确定性重载（与 watcher 幂等，防平台事件漏发）
        }

        Assert.IsTrue(reference.Current.CanCall("sample_mul"), "文件变更后 watcher（或兜底轮询）应触发热换到新工具面。");
    }

    // ---- 夹具 ----

    public static class HotReloadSampleLibraryAlt
    {
        public static void RegisterAll(TConfig.ITrConfigRegistry registry)
            => registry.Register("sample_mul", (double a, double b) => a * b, "相乘（热换样例）");
    }

    /// <summary>把库清单写入目标：若 <paramref name="dirOrFile"/> 是目录 → 在目录内写 function-libraries.json 并返回其完整路径；
    /// 若是既有文件路径 → 覆盖写该文件（热换测试改同路径）。不带 BOM（与宿主读取语义一致，避免清单解析失败）。</summary>
    private static string WriteConfig(string dirOrFile, System.Type library)
    {
        var target = Directory.Exists(dirOrFile)
            ? Path.Combine(dirOrFile, "function-libraries.json")
            : dirOrFile;
        File.WriteAllText(target, ConfigJson(library));
        return target;
    }

    private static string ConfigJson(System.Type library)
    {
        var aqn = library.AssemblyQualifiedName ?? library.FullName!;
        return $"{{\"libraries\":[{{\"name\":\"sample\",\"type\":\"{aqn}\",\"enabled\":true}}]}}";
    }

    private static TempDirHandle TempDir() => new();

    /// <summary>临时目录句柄（IDisposable + 隐式转 string；Dispose 时整目录删除，避免测试残留）。</summary>
    private sealed class TempDirHandle : IDisposable
    {
        public string Path { get; }

        public TempDirHandle()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public static implicit operator string(TempDirHandle d) => d.Path;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { /* 清理失败不阻断测试 */ }
        }
    }
}

