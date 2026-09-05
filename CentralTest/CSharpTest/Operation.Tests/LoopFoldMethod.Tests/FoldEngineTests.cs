using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.LoopFoldMethod.Cutoff;
using Operation.LoopFoldMethod.Fold;
using Operation.LoopFoldMethod.FoldHead;

namespace Operation.Tests.LoopFoldMethod;

/// <summary>
/// FoldEngine unfold 全分支测试：确定性假算子测截断 / 预算熔断 / 取消 / 收敛 / 空值守卫 / 上下文记账 / 观测。
/// 全部零 AI 额度、可在纯 CI 跑（LLM-free 关键收益）。
/// </summary>
[TestClass]
public class FoldEngineTests
{
    [TestMethod]
    public async Task BudgetExhausted_StopsAtMaxCoordinate()
    {
        var engine = new FoldEngine<int, int>();
        var result = await engine.RunAsync(
            initialState: 0,
            op: new IncrementOperator(),
            headProjector: new LastStateHeadProjector<int>(),
            cutoff: new BudgetCutoffPolicy<int>(),
            budget: new FoldBudget(3));

        Assert.AreEqual(FoldCut.BudgetExhausted, result.Cut);
        Assert.AreEqual(3, result.Coordinate);
        Assert.AreEqual(3, result.Vertices.Count);
        Assert.AreEqual(4, result.Trace.Count); // s0..s3
        Assert.AreEqual(3, result.Head);        // LastState = 3
    }

    [TestMethod]
    public async Task ConvergedHead_ReturnsSuccess()
    {
        var engine = new FoldEngine<int, int>();
        var result = await engine.RunAsync(
            initialState: 0,
            op: new ConstantOperator(0),
            headProjector: new LastStateHeadProjector<int>(),
            cutoff: new ConvergenceCutoffPolicy<int>(),
            budget: FoldBudget.Unlimited);

        Assert.AreEqual(FoldCut.Success, result.Cut);
        Assert.AreEqual(1, result.Coordinate);
        Assert.AreEqual(1, result.Vertices.Count);
    }

    [TestMethod]
    public async Task Cancelled_PreCancelledToken_ReturnsCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var engine = new FoldEngine<int, int>();
        var result = await engine.RunAsync(
            initialState: 0,
            op: new IncrementOperator(),
            headProjector: new LastStateHeadProjector<int>(),
            cutoff: new BudgetCutoffPolicy<int>(),
            budget: FoldBudget.Unlimited,
            cts.Token);

        Assert.AreEqual(FoldCut.Cancelled, result.Cut);
        Assert.AreEqual(0, result.Coordinate);
        Assert.AreEqual(1, result.Trace.Count); // 仅初始点 s0
    }

    [TestMethod]
    public async Task NullOperatorResult_TriggersDefensiveTrip()
    {
        var engine = new FoldEngine<int, int>();
        var result = await engine.RunAsync(
            initialState: 0,
            op: new NullOperator(),
            headProjector: new LastStateHeadProjector<int>(),
            cutoff: new BudgetCutoffPolicy<int>(),
            budget: FoldBudget.Unlimited);

        Assert.AreEqual(FoldCut.TripTerminated, result.Cut);
        Assert.AreEqual(0, result.Coordinate);
        Assert.AreEqual(0, result.Vertices.Count);
    }

    [TestMethod]
    public async Task Context_ReceivesCoordinateAndPreviousVertices()
    {
        var op = new RecordContextOperator();
        var engine = new FoldEngine<int, int>();
        await engine.RunAsync(
            initialState: 0,
            op: op,
            headProjector: new LastStateHeadProjector<int>(),
            cutoff: new BudgetCutoffPolicy<int>(),
            budget: new FoldBudget(3));

        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, op.SeenCoordinates);
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, op.SeenPreviousCounts);
    }

    [TestMethod]
    public async Task Observer_ReceivesVertexAndStateAdvance()
    {
        var observer = new RecordingObserver();
        var engine = new FoldEngine<int, int>();
        var result = await engine.RunAsync(
            initialState: 0,
            op: new IncrementOperator(),
            headProjector: new LastStateHeadProjector<int>(),
            cutoff: new BudgetCutoffPolicy<int>(),
            budget: new FoldBudget(2),
            observer: observer);

        Assert.AreEqual(2, observer.Vertices.Count);
        Assert.AreEqual(2, observer.StatesAdvanced.Count);
        Assert.AreEqual(FoldCut.BudgetExhausted, result.Cut);
    }
}
