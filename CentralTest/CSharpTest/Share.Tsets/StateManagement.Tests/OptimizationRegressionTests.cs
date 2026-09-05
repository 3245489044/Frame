using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using Share.StateManagement;
using Share.TaskStatusPool.Loops;
using TConfig.ContextFrame.Abstractions;
using StorageLayer = TConfig.StorageLayer;

namespace Share.StateManagement.Tests;

[TestClass]
public class StateTransactionRegressionTests
{
    private static IStorageCell CreateCell()
        => new StorageEngine("tx-" + Guid.NewGuid().ToString("N")).RootCell;

    [TestMethod]
    public void Commit_Preserves_SetRemove_Interleaving_LastOpWins()
    {
        var cell = CreateCell();
        var hub = new StateSubscriptionHub();
        var key = new StateKey("ns", "scope", "a");

        using var tx = new StateTransaction(cell, hub);
        tx.Remove(key);      // 先移除
        tx.Set(key, "v");    // 后写入 → 最后操作应为“写入”（E7）

        tx.Commit();

        Assert.IsTrue(cell.Contains(key.FullKey, StorageLayer.Session));
        Assert.AreEqual("v", cell.Get<string>(key.FullKey, StorageLayer.Session));
    }

    [TestMethod]
    public void Commit_Publishes_InOrder_Via_AutoVersion()
    {
        var cell = CreateCell();
        var hub = new StateSubscriptionHub();
        var events = new List<(string, object?)>();
        using var sub = hub.Subscribe("ns", "scope", (k, snap) => events.Add((k.Key, snap.Value)));

        using (var tx = new StateTransaction(cell, hub))
        {
            tx.Set(new StateKey("ns", "scope", "a"), "1");
            tx.Set(new StateKey("ns", "scope", "b"), "2");
            tx.Commit();
        }

        Assert.AreEqual(2, events.Count);
        Assert.AreEqual(("a", (object)"1"), events[0]);
        Assert.AreEqual(("b", (object)"2"), events[1]);
    }

    [TestMethod]
    public void Commit_Versions_Monotonic_AcrossTransactions()
    {
        var cell = CreateCell();
        var hub = new StateSubscriptionHub();
        var versions = new List<long>();
        using var sub = hub.Subscribe("ns", "scope", (_, snap) => versions.Add(snap.Version));

        using (var tx = new StateTransaction(cell, hub)) { tx.Set(new StateKey("ns", "scope", "a"), "1"); tx.Commit(); }
        using (var tx = new StateTransaction(cell, hub)) { tx.Set(new StateKey("ns", "scope", "b"), "2"); tx.Commit(); }

        Assert.AreEqual(2, versions.Count);
        Assert.IsTrue(versions[0] < versions[1]);
    }
}

[TestClass]
public class LoopDeciderRegressionTests
{
    private sealed class TestLoopState : ILoopState
    {
        public FailureBudget Budget { get; } = new();
        public ProgressToken Token { get; } = new();
        public ControlFlag Flag { get; } = new();
        public int Round { get; set; }
        public string? UserInput { get; set; }
        public string? LastResultText { get; set; }
        public EvalOutcome LastEval { get; set; }
        public IDictionary<string, object> Data { get; } = new Dictionary<string, object>();
    }

    [TestMethod]
    public void BudgetEscalator_FailureError_NotNeutralized_ByPassEval()
    {
        var state = new TestLoopState();
        // 失败升级与成功衰减互斥：不应先升后降抵消（E5）
        BudgetEscalator.Apply(state, ErrorClass.ExecutionFailure, EvalOutcome.Pass);
        Assert.AreEqual(1, state.Budget.Level);
    }

    [TestMethod]
    public void BudgetEscalator_PartialOutput_DoesNotEscalate()
    {
        var state = new TestLoopState();
        BudgetEscalator.Apply(state, ErrorClass.PartialOutput, EvalOutcome.Partial);
        Assert.AreEqual(0, state.Budget.Level);
    }

    [TestMethod]
    public void BudgetEscalator_Pass_NoError_Deescalates()
    {
        var state = new TestLoopState();
        state.Budget.Escalate(); // level 1
        BudgetEscalator.Apply(state, null, EvalOutcome.Pass);
        Assert.AreEqual(0, state.Budget.Level);
    }

    [TestMethod]
    public void BudgetEscalator_Irrecoverable_TripsAndHalts()
    {
        var state = new TestLoopState();
        BudgetEscalator.Apply(state, ErrorClass.Irrecoverable, EvalOutcome.Fail);
        Assert.AreEqual(3, state.Budget.Level);
        Assert.IsTrue(state.Flag.Halt);
    }
}

[TestClass]
public class RetryPolicyRegressionTests
{
    [TestMethod]
    public void GetDelayMs_Clamps_Overflow_ToMaxInt()
    {
        var policy = new RetryPolicy { RetryDelayMs = int.MaxValue, RetryDelayMultiplier = 1000 };
        Assert.AreEqual(int.MaxValue, policy.GetDelayMs(5));
    }

    [TestMethod]
    public void GetDelayMs_NeverNegative()
    {
        var policy = new RetryPolicy { RetryDelayMs = -100, RetryDelayMultiplier = 2.0 };
        Assert.AreEqual(0, policy.GetDelayMs(0));
    }

    [TestMethod]
    public void GetDelayMs_ExponentialBackoff()
    {
        var policy = new RetryPolicy { RetryDelayMs = 100, RetryDelayMultiplier = 2.0 };
        Assert.AreEqual(100, policy.GetDelayMs(0));
        Assert.AreEqual(200, policy.GetDelayMs(1));
        Assert.AreEqual(400, policy.GetDelayMs(2));
    }
}

[TestClass]
public class StoreConsolidationRegressionTests
{
    [TestMethod]
    public void InMemoryStateStore_Behavior_Preserved()
    {
        var store = new InMemoryStateStore();
        store.Set("a", "1");
        Assert.IsTrue(store.Has("a"));
        Assert.AreEqual("1", store.Get("a"));
        Assert.AreEqual(1, store.Count);
        Assert.IsFalse(store.IsEmpty);

        store.Remove("a");
        Assert.IsNull(store.Get("a"));
        Assert.AreEqual(0, store.Count);
        Assert.IsTrue(store.IsEmpty);
    }

    [TestMethod]
    public void VirtualStore_Behavior_Preserved()
    {
        var store = new VirtualStore();
        store.SetValue("a", "1");
        store.SetValue("b", "2");
        Assert.IsTrue(store.HasControl("a"));
        Assert.AreEqual("1", store.GetValue("a"));
        Assert.AreEqual(2, store.Count);

        store.RemoveByPrefix("a");
        Assert.IsFalse(store.HasControl("a"));
        Assert.IsTrue(store.HasControl("b"));
        Assert.AreEqual(1, store.Count);
    }

    [TestMethod]
    public void VirtualStore_GetOrAdd_Adds_Then_Gets()
    {
        var store = new VirtualStore();
        Assert.AreEqual("d", store.GetOrAdd("k", "d"));
        Assert.AreEqual("d", store.GetOrAdd("k", "ignored"));
        Assert.AreEqual(1, store.Count);
    }
}
