using CallCenter.Core.Cache;

namespace CallCenter.Tests;

/// <summary>锁定 P4 编译缓存：fingerprint 命中语义 + 线程安全的增删清。</summary>
[TestClass]
public class BuildCacheTests
{
    [TestMethod]
    public void Put_Then_TryGet_Returns_Artifact()
    {
        var cache = new BuildCache();
        cache.Put("fpA", new { il = "builtA" });

        Assert.IsTrue(cache.TryGet("fpA", out var artifact));
        Assert.IsNotNull(artifact);
        Assert.AreEqual(1, cache.Count);
    }

    [TestMethod]
    public void Missing_Key_Returns_False()
    {
        var cache = new BuildCache();
        Assert.IsFalse(cache.TryGet("nope", out _));
        Assert.AreEqual(0, cache.Count);
    }

    [TestMethod]
    public void Put_Same_Key_Overwrites()
    {
        var cache = new BuildCache();
        cache.Put("fpA", "v1");
        cache.Put("fpA", "v2");

        Assert.IsTrue(cache.TryGet("fpA", out var artifact));
        Assert.AreEqual("v2", artifact);
        Assert.AreEqual(1, cache.Count);
    }

    [TestMethod]
    public void Remove_And_Clear()
    {
        var cache = new BuildCache();
        cache.Put("fpA", 1);
        cache.Put("fpB", 2);

        Assert.IsTrue(cache.Remove("fpA"));
        Assert.AreEqual(1, cache.Count);

        cache.Clear();
        Assert.AreEqual(0, cache.Count);
    }

    [TestMethod]
    public void Concurrent_Access_Does_Not_Lose_Items()
    {
        var cache = new BuildCache();
        Parallel.For(0, 200, i => cache.Put("fp" + i, i));
        Assert.AreEqual(200, cache.Count);
    }
}
