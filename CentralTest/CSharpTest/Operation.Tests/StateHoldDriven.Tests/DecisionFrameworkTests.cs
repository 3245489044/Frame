using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using Operation.StateBlock.Core;
using Operation.StateBlock.Decision;
using Share.StateManagement;
using Share.TaskStatusPool.Loops;
using Share.TaskStatusPool.Routing;

namespace StateHoldDriven.Tests;

/// <summary>
/// 决策词 ↔ LoopDecision 词表单源（<see cref="DecisionWords"/>）投影等价单测。
/// 词表登记下沉后全库唯一（R9），Agent 侧 DecisionWordClassifier 与决策记录投影均委托本类（SingleImplementation / TH-12）。
/// 对应方案 P1「类型单测：Word/Kind 投影等价」。
/// </summary>
[TestClass]
public class DecisionWordsTests
{
    [TestMethod]
    public void ContinueWord_MapsTo_Continue() => Assert.AreEqual(LoopDecision.Continue, DecisionWords.Map(DecisionWords.ContinueWord));

    [TestMethod]
    public void StopAndComplete_MapsTo_Success()
    {
        Assert.AreEqual(LoopDecision.Success, DecisionWords.Map(DecisionWords.StopWord));
        Assert.AreEqual(LoopDecision.Success, DecisionWords.Map(DecisionWords.CompleteWord));
    }

    [TestMethod]
    public void Map_IsCaseAndWhitespaceInsensitive()
    {
        Assert.AreEqual(LoopDecision.Success, DecisionWords.Map("  STOP "));
        Assert.AreEqual(LoopDecision.Continue, DecisionWords.Map("Continue"));
        Assert.IsTrue(DecisionWords.IsSuccess("Complete"));
    }

    [TestMethod]
    public void Error_MapsToNull_ButIsError()
    {
        Assert.IsNull(DecisionWords.Map(DecisionWords.ErrorWord));
        Assert.IsTrue(DecisionWords.IsError(DecisionWords.ErrorWord));
        Assert.IsFalse(DecisionWords.IsSuccess(DecisionWords.ErrorWord));
        Assert.IsFalse(DecisionWords.IsContinue(DecisionWords.ErrorWord));
    }

    [TestMethod]
    public void UnknownOrEmpty_MapsToNull_ButIsUnknown()
    {
        Assert.IsNull(DecisionWords.Map("whatever"));
        Assert.IsNull(DecisionWords.Map(null));
        Assert.IsNull(DecisionWords.Map("   "));
        Assert.IsTrue(DecisionWords.IsUnknown("whatever"));
        Assert.IsTrue(DecisionWords.IsUnknown(null));
        Assert.IsFalse(DecisionWords.IsSuccess(null));
    }
}

/// <summary>
/// 轮决策记录（<see cref="DecisionRecord"/>）构造/字段往返单测（决策一等化，R7 语义权威在 Share，本记录仅投影）。
/// </summary>
[TestClass]
public class DecisionRecordTests
{
    [TestMethod]
    public void Record_Carries_AllAuditFields()
    {
        var rec = new DecisionRecord
        {
            Decision = LoopDecision.Success,
            Word = DecisionWords.StopWord,
            Eval = EvalOutcome.Pass,
            Error = null,
            Reason = "goal reached",
            Source = "skeleton",
        };

        Assert.AreEqual(LoopDecision.Success, rec.Decision);
        Assert.AreEqual(DecisionWords.StopWord, rec.Word);
        Assert.AreEqual(EvalOutcome.Pass, rec.Eval);
        Assert.IsNull(rec.Error);
        Assert.AreEqual("goal reached", rec.Reason);
        Assert.AreEqual("skeleton", rec.Source);
        Assert.IsTrue(rec.At <= DateTimeOffset.Now);
    }

    [TestMethod]
    public void TimeoutDecision_CanCarry_ErrorClassAndReason()
    {
        var rec = new DecisionRecord
        {
            Decision = LoopDecision.Timeout,
            Eval = EvalOutcome.Fail,
            Error = ErrorClass.Timeout,
            Reason = "real timeout",
            Source = "engine",
        };

        Assert.AreEqual(LoopDecision.Timeout, rec.Decision);
        Assert.AreEqual(ErrorClass.Timeout, rec.Error);
        Assert.AreEqual("real timeout", rec.Reason);
    }
}

/// <summary>
/// 决策中心门面（<see cref="Operation.StateBlock.Decision.DecisionCenter"/>）审计/回放单测：决策记录落 Center + 最近投影。
/// </summary>
[TestClass]
public class DecisionCenterTests
{
    private static IStateCenter CreateCenter() => new StateCenter(new StorageCell("center"));

    [TestMethod]
    public void Record_ThenRecent_ReturnsSameDecisionProjection()
    {
        var center = CreateCenter();
        var dc = new Operation.StateBlock.Decision.DecisionCenter(center);
        Assert.IsNull(dc.Recent("taskX")); // 无记录

        dc.Record("taskX", new DecisionRecord
        {
            Decision = LoopDecision.Success,
            Word = DecisionWords.StopWord,
            Eval = EvalOutcome.Pass,
            Error = null,
            Reason = "goal reached",
            Source = "skeleton",
        });

        var recent = dc.Recent("taskX");
        Assert.IsNotNull(recent);
        Assert.AreEqual(LoopDecision.Success, recent!.Decision);
        Assert.AreEqual(DecisionWords.StopWord, recent.Word);
        Assert.AreEqual(EvalOutcome.Pass, recent.Eval);
        Assert.IsNull(recent.Error);
        Assert.AreEqual("goal reached", recent.Reason);
        Assert.AreEqual("skeleton", recent.Source);
    }

    [TestMethod]
    public void Recent_IsScopedPerTask_AndOverwritable()
    {
        var center = CreateCenter();
        var dc = new Operation.StateBlock.Decision.DecisionCenter(center);
        dc.Record("taskA", new DecisionRecord { Decision = LoopDecision.Timeout, Eval = EvalOutcome.Fail, Reason = "t1" });
        dc.Record("taskB", new DecisionRecord { Decision = LoopDecision.Continue, Eval = EvalOutcome.Partial, Reason = "c1" });

        Assert.AreEqual(LoopDecision.Timeout, dc.Recent("taskA")!.Decision);
        Assert.AreEqual(LoopDecision.Continue, dc.Recent("taskB")!.Decision);

        // 覆盖最近记录（轮末最新）
        dc.Record("taskA", new DecisionRecord { Decision = LoopDecision.Success, Eval = EvalOutcome.Pass, Reason = "t2" });
        Assert.AreEqual(LoopDecision.Success, dc.Recent("taskA")!.Decision);
        Assert.AreEqual("t2", dc.Recent("taskA")!.Reason);
        Assert.AreEqual(LoopDecision.Continue, dc.Recent("taskB")!.Decision); // B 不受影响
    }
}
