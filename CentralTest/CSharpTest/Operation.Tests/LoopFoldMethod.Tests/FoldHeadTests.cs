using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.LoopFoldMethod.Cutoff;
using Operation.LoopFoldMethod.Fold;
using Operation.LoopFoldMethod.FoldHead;

namespace Operation.Tests.LoopFoldMethod;

/// <summary>
/// 折叠头（FoldHead）正确性测试：假 IFoldHeadProjector 测「前缀 → 判定点」折叠（旧引擎缺失的抽象）。
/// </summary>
[TestClass]
public class FoldHeadTests
{
    [TestMethod]
    public async Task LastStateHead_ProjectsLastPoint()
    {
        var engine = new FoldEngine<int, int>();
        var result = await engine.RunAsync(
            initialState: 0,
            op: new IncrementOperator(),
            headProjector: new LastStateHeadProjector<int>(),
            cutoff: new BudgetCutoffPolicy<int>(),
            budget: new FoldBudget(3));

        Assert.AreEqual(3, result.Head); // trace [0,1,2,3] → 末位 3
    }

    [TestMethod]
    public async Task IdentityHead_ProjectsInitialPoint()
    {
        var engine = new FoldEngine<int, int>();
        var result = await engine.RunAsync(
            initialState: 0,
            op: new IncrementOperator(),
            headProjector: new IdentityHeadProjector<int>(),
            cutoff: new BudgetCutoffPolicy<int>(),
            budget: new FoldBudget(3));

        Assert.AreEqual(0, result.Head); // 恒等 → 初始点 s0
    }

    [TestMethod]
    public async Task CustomHead_FoldsPrefixToDecisionPoint()
    {
        var engine = new FoldEngine<int, int>();
        var result = await engine.RunAsync(
            initialState: 0,
            op: new IncrementOperator(),
            headProjector: new SumHeadProjector(),
            cutoff: new BudgetCutoffPolicy<int>(),
            budget: new FoldBudget(3));

        Assert.AreEqual(0 + 1 + 2 + 3, result.Head); // 求和折叠 [0,1,2,3] = 6
    }
}
