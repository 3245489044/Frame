using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.LoopFoldMethod.Cutoff;
using Operation.LoopFoldMethod.Fold;
using Operation.LoopFoldMethod.FoldHead;

namespace Operation.Tests.LoopFoldMethod;

/// <summary>
/// 信任反转的执法断言：算子失控时折叠器必截断（不信任外部算子自停）。
/// 用假 ICutoffPolicy / 预算策略测「折叠器对不可信外部算子的强制执法层」。
/// </summary>
[TestClass]
public class CutoffEnforcementTests
{
    /// <summary>假截断策略：坐标 ≥ tripAfter 即熔断（确定性，验证执法成立）。</summary>
    private sealed class FakeTripCutoff : ICutoffPolicy<int>
    {
        private readonly int _tripAfter;
        public FakeTripCutoff(int tripAfter) => _tripAfter = tripAfter;
        public FoldCut Decide(int head, int coordinate, FoldBudget budget)
            => coordinate >= _tripAfter ? FoldCut.TripTerminated : FoldCut.Continue;
    }

    [TestMethod]
    public async Task RunawayOperator_IsCutOffByFakePolicy_NoSelfStop()
    {
        // 失控算子：每步产出新状态、永不自停（IncrementOperator 无限递增）；
        // 折叠器必须经 ICutoffPolicy 强制熔断，而非依赖算子自停（信任反转）。
        var engine = new FoldEngine<int, int>();
        var result = await engine.RunAsync(
            initialState: 0,
            op: new IncrementOperator(),
            headProjector: new LastStateHeadProjector<int>(),
            cutoff: new FakeTripCutoff(4),
            budget: FoldBudget.Unlimited);

        Assert.AreEqual(FoldCut.TripTerminated, result.Cut);
        Assert.AreEqual(4, result.Coordinate);
    }

    [TestMethod]
    public async Task RunawayOperator_IsCutOffByBudget()
    {
        var engine = new FoldEngine<int, int>();
        var result = await engine.RunAsync(
            initialState: 0,
            op: new IncrementOperator(),
            headProjector: new LastStateHeadProjector<int>(),
            cutoff: new BudgetCutoffPolicy<int>(),
            budget: new FoldBudget(5));

        Assert.AreEqual(FoldCut.BudgetExhausted, result.Cut);
        Assert.AreEqual(5, result.Coordinate);
        Assert.AreEqual(5, result.Vertices.Count);
    }
}
