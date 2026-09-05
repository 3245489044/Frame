using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using Operation.StateBlock.Core;
using Operation.StateBlock.Decision;
using Share.StateManagement;
using Share.TaskStatusPool.Loops;

namespace StateHoldDriven.Tests;

/// <summary>
/// 确定性核心状态化（P2 LoopStateStore）单测：Center Session 真源往返、单写口语义、RO 只读投影、初始默认、scope 隔离。
/// 对应文档 2026-09-04 StateHoldDriven §6 P2（单写、RO 只读、事务语义）。裁决语义权威仍在 Share（R7），本测试只验证状态落地层。
/// </summary>
[TestClass]
public class LoopStateStoreTests
{
    private static IStateCenter CreateCenter() => new StateCenter(new StorageCell("center"));

    private static void SetCore(ILoopState w, int budget, int tokenValue, int tokenMax, bool halt, int round, EvalOutcome eval)
    {
        DeterministicCoreCopier.CopyFromValues(w, budget, tokenValue, tokenMax, halt);
        w.Round = round;
        w.LastEval = eval;
    }

    [TestMethod]
    public void Save_ThenNewStoreReload_RoundTripsCoreFromCenterSource()
    {
        var center = CreateCenter();
        var s1 = new LoopStateStore(center, "taskA");
        var w = s1.LoadWorking();
        SetCore(w, budget: 2, tokenValue: 4, tokenMax: 10, halt: false, round: 5, eval: EvalOutcome.Pass);
        s1.Save(w);

        // 新 store 实例（同一 Center 真源）应还原一致值 —— 证明核心已落 Center 而非内存。
        var s2 = new LoopStateStore(center, "taskA");
        Assert.IsTrue(s2.HasPersisted());
        var view = s2.View();
        Assert.AreEqual(2, view.BudgetLevel);
        Assert.AreEqual(4, view.TokenValue);
        Assert.AreEqual(10, view.TokenMax);
        Assert.IsFalse(view.Halt);
        Assert.AreEqual(5, view.Round);
        Assert.AreEqual(EvalOutcome.Pass, view.LastEval);
        Assert.IsFalse(view.IsExhausted);

        var reloaded = s2.LoadWorking();
        Assert.AreEqual(2, reloaded.Budget.Level);
        Assert.AreEqual(4, reloaded.Token.Value);
        Assert.AreEqual(10, reloaded.Token.Max);
        Assert.AreEqual(5, reloaded.Round);
        Assert.AreEqual(EvalOutcome.Pass, reloaded.LastEval);
    }

    [TestMethod]
    public void Trip_IsReflectedInView()
    {
        var center = CreateCenter();
        var store = new LoopStateStore(center, "taskTrip");
        var w = store.LoadWorking();
        SetCore(w, budget: 0, tokenValue: 0, tokenMax: 3, halt: true, round: 1, eval: EvalOutcome.Fail);
        store.Save(w);
        Assert.IsTrue(store.View().IsTripped);
    }

    [TestMethod]
    public void LoadWorkingMutation_WithoutSave_DoesNotTouchCenterSource()
    {
        var center = CreateCenter();
        var store = new LoopStateStore(center, "taskRO");
        store.Reset(); // 持久化默认初始
        var before = store.View();

        var working = store.LoadWorking();
        working.Budget.Escalate(); // 越权改预算（本应被单写纪律约束）
        working.Token.Advance();

        var after = store.View(); // RO：未 Save 前真源不受影响（未提交语义）
        Assert.AreEqual(before, after);
        Assert.AreEqual(0, after.BudgetLevel);
        Assert.AreEqual(0, after.TokenValue);
    }

    [TestMethod]
    public void UnpersistedScope_ReturnsDefaultInitial_AndIsolatesByScope()
    {
        var center = CreateCenter();
        var storeA = new LoopStateStore(center, "scopeA");
        Assert.IsFalse(storeA.HasPersisted());
        var vA = storeA.View();
        Assert.AreEqual(1, vA.Round);
        Assert.AreEqual(0, vA.BudgetLevel);
        Assert.AreEqual(0, vA.TokenValue);
        Assert.AreEqual(3, vA.TokenMax);
        Assert.AreEqual(EvalOutcome.None, vA.LastEval);

        var wa = storeA.LoadWorking();
        Assert.AreEqual(1, wa.Round);

        // 写 A 不影响未持久化的 B
        var storeB = new LoopStateStore(center, "scopeB");
        Assert.IsFalse(storeB.HasPersisted());
        storeA.Save(storeA.LoadWorking());
        Assert.IsTrue(storeA.HasPersisted());
        Assert.IsFalse(storeB.HasPersisted());
    }
}
