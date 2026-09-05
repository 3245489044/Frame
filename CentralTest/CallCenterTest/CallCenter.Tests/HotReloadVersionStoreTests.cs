using CallCenter.Core.Cache;

namespace CallCenter.Tests;

/// <summary>
/// 锁定 P4 热加载版本语义：新版本激活 → 旧版保在途 → 计数归零可回收；路由永远指向激活版本。
/// （文件监视 / 重编译 / CSG 一致性校验是上层驱动，本单测只验证版本/引用状态机。）
/// </summary>
[TestClass]
public class HotReloadVersionStoreTests
{
    [TestMethod]
    public void RegisterVersion_Sets_Active_To_Newest()
    {
        var store = new HotReloadVersionStore();
        store.RegisterVersion("cc_add", "fp-v1");
        store.RegisterVersion("cc_add", "fp-v2");

        Assert.AreEqual("fp-v2", store.ActiveFingerprint("cc_add"));
        CollectionAssert.AreEqual(new[] { "fp-v1", "fp-v2" }, store.Versions("cc_add").ToArray());
    }

    [TestMethod]
    public void Old_Version_With_InFlight_Is_Not_Drainable_Until_Zero()
    {
        var store = new HotReloadVersionStore();
        store.RegisterVersion("cc_add", "fp-v1");
        store.RegisterVersion("cc_add", "fp-v2"); // 热切换：v1 → v2

        Assert.IsTrue(store.IsDrainable("cc_add", "fp-v1"), "v1 为旧版且无在途 → 可回收");

        store.BeginInFlight("cc_add", "fp-v1"); // 旧版在途调用进行中
        Assert.IsTrue(store.InFlightCount("cc_add", "fp-v1") == 1);
        Assert.IsFalse(store.IsDrainable("cc_add", "fp-v1"), "旧版有在途调用，不可回收");

        store.EndInFlight("cc_add", "fp-v1");
        Assert.IsTrue(store.IsDrainable("cc_add", "fp-v1"), "在途归零后可回收");
    }

    [TestMethod]
    public void Active_Version_Is_Never_Drainable()
    {
        var store = new HotReloadVersionStore();
        store.RegisterVersion("cc_add", "fp-v1");

        Assert.IsFalse(store.IsDrainable("cc_add", "fp-v1"));
    }

    [TestMethod]
    public void PruneDrained_Removes_Only_Drained_Old_Versions()
    {
        var store = new HotReloadVersionStore();
        store.RegisterVersion("cc_add", "fp-v1");
        store.RegisterVersion("cc_add", "fp-v2");
        store.RegisterVersion("cc_add", "fp-v3");

        store.BeginInFlight("cc_add", "fp-v1"); // v1 保在途 → 不可回收
        // v2 无在途旧版 → 可回收
        Assert.AreEqual(1, store.PruneDrained("cc_add"), "仅 v2 被回收，v1(在途) 与 v3(激活) 保留");

        CollectionAssert.AreEqual(new[] { "fp-v1", "fp-v3" }, store.Versions("cc_add").ToArray());
        Assert.AreEqual("fp-v3", store.ActiveFingerprint("cc_add"));
    }

    [TestMethod]
    public void Remove_Drops_Whole_Tool_And_Restarts_Fresh()
    {
        var store = new HotReloadVersionStore();
        store.RegisterVersion("cc_add", "fp-v1");
        store.RegisterVersion("cc_add", "fp-v2");

        store.Remove("cc_add"); // B2/C3：工具退役（不再由任何轨路由）

        Assert.AreEqual(0, store.Versions("cc_add").Count);
        Assert.IsNull(store.ActiveFingerprint("cc_add"));

        // 退役后同名重新登记：从全新历史开始，不被旧版本污染（C3）。
        store.RegisterVersion("cc_add", "fresh");
        Assert.AreEqual("fresh", store.ActiveFingerprint("cc_add"));
        CollectionAssert.AreEqual(new[] { "fresh" }, store.Versions("cc_add").ToArray());
    }

    [TestMethod]
    public void End_InFlight_On_Unknown_Throws()
    {
        var store = new HotReloadVersionStore();
        ThrowsAssert.Throws<InvalidOperationException>(() => store.EndInFlight("cc_add", "fp-none"));
    }
}
