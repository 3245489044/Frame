using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using Operation.LoopFoldMethod.Cutoff;
using Operation.LoopFoldMethod.Fold;
using Operation.LoopFoldMethod.Operator;
using Operation.LoopFoldMethod.Signal;

namespace Operation.Tests.LoopFoldMethod;

/// <summary>
/// <see cref="RuleSignalResolver{TState,TVertex}"/> 决策信号化测试：无决策载荷回退纯表驱动；Continue 链推进；Stop/Error 终止；
/// Terminal 恒终止；Stop 覆盖链转移表。全部零 LLM、纯 CI 可跑。
/// </summary>
[TestClass]
public class RuleSignalResolverTests
{
    private static readonly IReadOnlyDictionary<string, string> Chain = new Dictionary<string, string>
    {
        ["start"] = "collect",
        ["collect"] = "analyze",
    };

    private static FoldContext<string> Ctx()
        => new(0, FoldBudget.Unlimited, System.Array.Empty<FoldVertex<string>>());

    private static FoldSignal<string> Signal(FoldSignalKind kind, string stepKey, FoldDecision? decision = null)
        => new(kind, stepKey, null, null, decision);

    [TestMethod]
    public void NoDecision_FallsBackToChainTable()
    {
        var r = new RuleSignalResolver<int, string>(Chain);
        var d = r.Resolve(Signal(FoldSignalKind.StepCompleted, "start"), 0, Ctx());
        Assert.IsNotNull(d);
        Assert.AreEqual("collect", d!.ExecuteStepKey);
    }

    [TestMethod]
    public void DecisionContinue_AdvancesChain()
    {
        var r = new RuleSignalResolver<int, string>(Chain);
        var d = r.Resolve(Signal(FoldSignalKind.StepCompleted, "start", FoldDecision.Continue), 0, Ctx());
        Assert.IsNotNull(d);
        Assert.AreEqual("collect", d!.ExecuteStepKey);
    }

    [TestMethod]
    public void DecisionStop_Terminates_EvenWithChainNext()
    {
        var r = new RuleSignalResolver<int, string>(Chain);
        // start→collect 有下一步，但 Decision=Stop → 链终点终止。
        var d = r.Resolve(Signal(FoldSignalKind.StepCompleted, "start", FoldDecision.Stop), 0, Ctx());
        Assert.IsNull(d);
    }

    [TestMethod]
    public void DecisionError_Terminates()
    {
        var r = new RuleSignalResolver<int, string>(Chain);
        var d = r.Resolve(Signal(FoldSignalKind.StepCompleted, "start", FoldDecision.Error), 0, Ctx());
        Assert.IsNull(d);
    }

    [TestMethod]
    public void Terminal_AlwaysTerminates_EvenWithContinue()
    {
        var r = new RuleSignalResolver<int, string>(Chain);
        var d = r.Resolve(Signal(FoldSignalKind.Terminal, "start", FoldDecision.Continue), 0, Ctx());
        Assert.IsNull(d);
    }

    [TestMethod]
    public void KeyNotInChain_Terminates()
    {
        var r = new RuleSignalResolver<int, string>(Chain);
        var d = r.Resolve(Signal(FoldSignalKind.StepCompleted, "act", FoldDecision.Continue), 0, Ctx());
        Assert.IsNull(d);
    }
}
