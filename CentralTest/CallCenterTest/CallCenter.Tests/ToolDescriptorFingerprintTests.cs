using CallCenter.Core.Tools;

namespace CallCenter.Tests;

/// <summary>锁定 ToolDescriptor.Fingerprint：同描述稳定、关键字段变化即变（编译缓存/热加载的 key 前提）。</summary>
[TestClass]
public class ToolDescriptorFingerprintTests
{
    private static ToolDescriptor Make(string? id = null, string name = "cc_echo", int version = 1, string backend = "CSharp")
        => new(
            Id: id ?? $"tool:{backend.ToLowerInvariant()}:{name}",
            Name: name, Backend: backend, Version: version,
            Description: "回显",
            new Dictionary<string, object?>(),
            new Dictionary<string, object?>(),
            ToolFlags.Deterministic | ToolFlags.HotReloadable);

    [TestMethod]
    public void Identical_Descriptors_Share_Fingerprint()
    {
        Assert.AreEqual(Make().Fingerprint, Make().Fingerprint);
        Assert.AreEqual(16, Make().Fingerprint.Length, "Fingerprint 应为定长十六进制摘要");
    }

    [TestMethod]
    public void Version_Change_Changes_Fingerprint()
        => Assert.AreNotEqual(Make(version: 1).Fingerprint, Make(version: 2).Fingerprint);

    [TestMethod]
    public void Name_Change_Changes_Fingerprint()
        // name 变化 → Id（tool:{backend}:{name}）随之变化 → fingerprint 区分。
        => Assert.AreNotEqual(Make(name: "cc_echo").Fingerprint, Make(name: "cc_add").Fingerprint);

    [TestMethod]
    public void Backend_Change_Changes_Fingerprint()
        => Assert.AreNotEqual(Make(backend: "CSharp").Fingerprint, Make(backend: "TypeScript").Fingerprint);

    [TestMethod]
    public void Id_Is_Part_Of_Fingerprint_Input()
        // 同名同 backend 但 Id 前缀不同（显式跨轨）→ 指纹区分。
        => Assert.AreNotEqual(
            Make(name: "cc_echo", backend: "TypeScript").Fingerprint,
            Make(name: "cc_echo", backend: "CSharp").Fingerprint);
}
