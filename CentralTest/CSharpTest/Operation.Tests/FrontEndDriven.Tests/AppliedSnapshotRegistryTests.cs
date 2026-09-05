
using System.Collections.Generic;
using FrontEndDriven.WinForms.Applier;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TConfig.ECS;

namespace FrontEndDriven.Tests;

/// <summary>
/// R1·AppliedSnapshot 目标快照注册表单测：
/// 纯数据读写（无 WinForms/ECS 运行时依赖），覆盖 Set/TryGet/覆盖/移除/清空/隔离性/实体隔离。
/// 生命周期清理（EntityDestroying → 移除快照）经代码审查验证（一行 TryRemove，随 EntityManager 事件，
/// 与 ControlPool 同模式——实体销毁自动清理，杜绝残留快照 → 虚假差异）。
/// </summary>
[TestClass]
public class AppliedSnapshotRegistryTests
{
    private static Entity E(int id) => new(id, 1);

    [TestMethod]
    public void Set_Then_TryGet_ReturnsSnapshot()
    {
        var registry = new AppliedSnapshotRegistry();
        var entity = E(1);
        var snapshot = new Dictionary<string, object?> { ["Left"] = 10, ["Text"] = "hello" };

        registry.Set(entity, snapshot);

        Assert.IsTrue(registry.TryGet(entity, out var actual), "Set 后应可读取快照");
        Assert.AreEqual(10, actual["Left"]);
        Assert.AreEqual("hello", actual["Text"]);
        Assert.AreEqual(1, registry.Count);
    }

    [TestMethod]
    public void Set_Overwrites_PreviousSnapshot()
    {
        var registry = new AppliedSnapshotRegistry();
        var entity = E(1);
        registry.Set(entity, new Dictionary<string, object?> { ["Left"] = 10 });
        registry.Set(entity, new Dictionary<string, object?> { ["Left"] = 20 });

        Assert.IsTrue(registry.TryGet(entity, out var actual));
        Assert.AreEqual(20, actual["Left"]);
        Assert.AreEqual(1, registry.Count, "同实体覆盖不应增加条目数");
    }

    [TestMethod]
    public void TryGet_Missing_ReturnsFalse()
    {
        var registry = new AppliedSnapshotRegistry();

        Assert.IsFalse(registry.TryGet(E(42), out _));
    }

    [TestMethod]
    public void Remove_DeletesSnapshot()
    {
        var registry = new AppliedSnapshotRegistry();
        var entity = E(1);
        registry.Set(entity, new Dictionary<string, object?> { ["Text"] = "x" });

        Assert.IsTrue(registry.Remove(entity));
        Assert.IsFalse(registry.TryGet(entity, out _));
        Assert.AreEqual(0, registry.Count);
    }

    [TestMethod]
    public void Clear_EmptiesAll()
    {
        var registry = new AppliedSnapshotRegistry();
        registry.Set(E(1), new Dictionary<string, object?> { ["Text"] = "a" });
        registry.Set(E(2), new Dictionary<string, object?> { ["Text"] = "b" });

        registry.Clear();

        Assert.AreEqual(0, registry.Count);
    }

    [TestMethod]
    public void Set_Isolates_ExternalMutation()
    {
        var registry = new AppliedSnapshotRegistry();
        var entity = E(1);
        var source = new Dictionary<string, object?> { ["Left"] = 10 };
        registry.Set(entity, source);

        // 外部后续修改源字典不应污染已存快照（快照为应用时点的不可变副本）
        source["Left"] = 999;

        Assert.IsTrue(registry.TryGet(entity, out var actual));
        Assert.AreEqual(10, actual["Left"]);
    }

    [TestMethod]
    public void DistinctEntities_DoNotInterfere()
    {
        var registry = new AppliedSnapshotRegistry();
        registry.Set(E(1), new Dictionary<string, object?> { ["Left"] = 10 });
        registry.Set(E(2), new Dictionary<string, object?> { ["Left"] = 20 });

        Assert.IsTrue(registry.TryGet(E(2), out var s2));
        Assert.AreEqual(20, s2["Left"]);
        Assert.IsTrue(registry.TryGet(E(1), out var s1));
        Assert.AreEqual(10, s1["Left"]);
    }
}
