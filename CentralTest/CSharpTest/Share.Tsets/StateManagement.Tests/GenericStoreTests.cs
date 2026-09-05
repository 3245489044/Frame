using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.StateManagement;

namespace Share.StateManagement.Tests;

/// <summary>
/// 收敛后的单一泛型 KV 存储（Core/GenericStore.cs）与 TTL 缓存（Core/TtlCache.cs）行为锁定。
/// P1/P4：删除 StateStorage 下的重复实现后，本文件为这两个 canonical 类型的回归保护。
/// </summary>
[TestClass]
public class GenericStoreTests
{
    [TestMethod]
    public void InMemoryStore_Stores_Retrieves_And_Raises_Changed()
    {
        var store = new InMemoryStore<string, int>();
        var changes = new List<StoreChangeType>();

        store.Changed += (_, e) => changes.Add(e.ChangeType);

        store.Store("a", 1);
        store.Store("a", 2); // 更新
        store.Remove("a");

        Assert.AreEqual(0, store.Retrieve("b")); // 不存在返回 default
        CollectionAssert.AreEqual(
            new[] { StoreChangeType.Added, StoreChangeType.Updated, StoreChangeType.Removed },
            changes.ToArray());
    }

    [TestMethod]
    public void InMemoryStore_GetOrAdd_Uses_Factory_Once()
    {
        var store = new InMemoryStore<string, int>();
        var factoryCalls = 0;

        var v1 = store.GetOrAdd("k", _ => { factoryCalls++; return 42; });
        var v2 = store.GetOrAdd("k", _ => { factoryCalls++; return 99; });

        Assert.AreEqual(42, v1);
        Assert.AreEqual(42, v2);
        Assert.AreEqual(1, factoryCalls, "已存在的键不应再次调用工厂");
        Assert.AreEqual(1, store.Count);
    }

    [TestMethod]
    public void InMemoryStore_Range_And_Keys()
    {
        var store = new InMemoryStore<string, int>();
        store.StoreRange(new[]
        {
            new KeyValuePair<string, int>("a", 1),
            new KeyValuePair<string, int>("b", 2),
        });

        CollectionAssert.AreEqual(new[] { "a", "b" }, store.GetAllKeys().OrderBy(k => k).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2 }, store.GetAllValues().OrderBy(v => v).ToArray());

        Assert.IsTrue(store.Contains("a"));
        store.Clear();
        Assert.AreEqual(0, store.Count);
    }
}

[TestClass]
public class TtlCacheCanonicalTests
{
    [TestMethod]
    public void TtlCache_Expires_Entries()
    {
        var cache = new TtlCache<string, string>(defaultTtl: TimeSpan.FromMilliseconds(30));

        cache.Set("a", "1");
        cache.Set("b", "2", TimeSpan.FromMinutes(10)); // 长 TTL 保留

        Assert.AreEqual("1", cache.Get("a"));
        Assert.AreEqual("2", cache.Get("b"));

        Thread.Sleep(80);

        Assert.IsNull(cache.Get("a"), "短 TTL 条目应过期");
        Assert.AreEqual("2", cache.Get("b"), "长 TTL 条目应保留");
        Assert.AreEqual(1, cache.ActiveCount);
    }

    [TestMethod]
    public void TtlCache_Statistics_Track_Hit_Miss()
    {
        var cache = new TtlCache<string, string>(defaultTtl: TimeSpan.FromMinutes(5));
        cache.Set("a", "1");

        cache.Get("a"); // hit
        cache.Get("missing"); // miss

        var stats = cache.GetStatistics();
        Assert.AreEqual(1, stats.HitCount);
        Assert.AreEqual(1, stats.MissCount);
        Assert.IsTrue(stats.HitRate > 0);
    }

    [TestMethod]
    public void TtlCache_ClearExpired_Returns_Count()
    {
        var cache = new TtlCache<string, string>(defaultTtl: TimeSpan.FromMilliseconds(20));
        cache.Set("a", "1");
        cache.Set("b", "2");
        cache.Set("c", "3", TimeSpan.FromMinutes(1));

        Thread.Sleep(60);

        var removed = cache.ClearExpired();
        Assert.AreEqual(2, removed);
        Assert.AreEqual(1, cache.Count);
    }
}
