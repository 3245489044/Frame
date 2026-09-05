using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.LoopFoldMethod.Cutoff;
using Operation.LoopFoldMethod.Fold;
using Operation.LoopFoldMethod.FoldHead;
using Operation.LoopFoldMethod.Signal;
using Operation.LoopFoldMethod.Attempt;

namespace Operation.Tests.LoopFoldMethod;

/// <summary>
/// SignalDrivenFoldEngine 信号驱动步进全分支测试：链推进（run_to_end）/ 链终点收敛 / 预算截断 / 取消终止 / 观测。
/// 验证「推进唯一方式 = 信号驱动步进」（ExternalInput 首信号 → StepCompleted 链推进 → 链终点终止）。
/// 全部零 AI 额度、可在纯 CI 跑（LLM-free 关键收益）。
/// </summary>
[TestClass]
public class SignalDrivenFoldEngineTests
{
    private static readonly IReadOnlyDictionary<string, string> Chain = new Dictionary<string, string>
    {
        ["start"] = "collect",
        ["collect"] = "analyze",
        ["analyze"] = "act",
    };

    [TestMethod]
    public async Task ExternalInput_DrivesWholeChain_EndsAtRunToEnd()
    {
        var op = new RecordingSignalOperator();
        var engine = new SignalDrivenFoldEngine<int, string>();

        var result = await engine.RunAsync(
            initialState: 0,
            initialSignal: new FoldSignal<string>(FoldSignalKind.ExternalInput, "start", "用户消息"),
            resolver: new ChainSignalResolver<int, string>(Chain),
            op: op,
            headProjector: new LastStateHeadProjector<int>(),
            cutoff: new BudgetCutoffPolicy<int>(),
            budget: FoldBudget.Unlimited);

        // 链终点（act 无 on_success）→ run_to_end 收敛成功；恰好推进 3 步。
        Assert.AreEqual(FoldCut.Success, result.Cut);
        Assert.AreEqual(3, result.Coordinate);
        Assert.AreEqual(3, result.Vertices.Count);
        CollectionAssert.AreEqual(new[] { "collect", "analyze", "act" }, op.ExecutedSteps);
        Assert.AreEqual(3, result.Head); // 初始 0 + 3 步 = 3
        // 首信号是 ExternalInput（start）；其后算子收到 StepCompleted 链推进信号（collect→驱动 analyze，analyze→驱动 act）。
        Assert.AreEqual(FoldSignalKind.ExternalInput, op.SeenSignals[0].Kind);
        CollectionAssert.AreEqual(
            op.SeenSignals.Skip(1).Select(s => s.StepKey).ToArray(),
            new[] { "collect", "analyze" });
    }

    [TestMethod]
    public async Task StepCompleted_DrivesNextStep_NotWholeChain()
    {
        // 单步链：start→collect，collect 无 next → 只执行 collect 一步即收敛。
        var single = new Dictionary<string, string> { ["start"] = "collect" };
        var op = new RecordingSignalOperator();
        var engine = new SignalDrivenFoldEngine<int, string>();

        var result = await engine.RunAsync(
            initialState: 0,
            initialSignal: new FoldSignal<string>(FoldSignalKind.ExternalInput, "start", null),
            resolver: new ChainSignalResolver<int, string>(single),
            op: op,
            headProjector: new LastStateHeadProjector<int>(),
            cutoff: new BudgetCutoffPolicy<int>(),
            budget: FoldBudget.Unlimited);

        Assert.AreEqual(FoldCut.Success, result.Cut);
        Assert.AreEqual(1, result.Coordinate);
        CollectionAssert.AreEqual(new[] { "collect" }, op.ExecutedSteps);
    }

    [TestMethod]
    public async Task ChainEnd_SignalIsTerminal_StopsImmediately()
    {
        var op = new RecordingSignalOperator();
        var engine = new SignalDrivenFoldEngine<int, string>();

        var result = await engine.RunAsync(
            initialState: 0,
            initialSignal: new FoldSignal<string>(FoldSignalKind.Terminal, "act"),
            resolver: new ChainSignalResolver<int, string>(Chain),
            op: op,
            headProjector: new LastStateHeadProjector<int>(),
            cutoff: new BudgetCutoffPolicy<int>(),
            budget: FoldBudget.Unlimited);

        // Terminal 信号 → resolver 恒 null → 0 步直接收敛成功。
        Assert.AreEqual(FoldCut.Success, result.Cut);
        Assert.AreEqual(0, result.Coordinate);
        Assert.AreEqual(0, op.ExecutedSteps.Count);
    }

    [TestMethod]
    public async Task BudgetExhausted_StopsBeforeChainEnd()
    {
        var op = new RecordingSignalOperator();
        var engine = new SignalDrivenFoldEngine<int, string>();

        var result = await engine.RunAsync(
            initialState: 0,
            initialSignal: new FoldSignal<string>(FoldSignalKind.ExternalInput, "start", null),
            resolver: new ChainSignalResolver<int, string>(Chain),
            op: op,
            headProjector: new LastStateHeadProjector<int>(),
            cutoff: new BudgetCutoffPolicy<int>(),
            budget: new FoldBudget(2));

        Assert.AreEqual(FoldCut.BudgetExhausted, result.Cut);
        Assert.AreEqual(2, result.Coordinate);
        CollectionAssert.AreEqual(new[] { "collect", "analyze" }, op.ExecutedSteps);
    }

    [TestMethod]
    public async Task Cancelled_PreCancelledToken_ReturnsCancelled_NoSteps()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var op = new CancellingSignalOperator();
        var engine = new SignalDrivenFoldEngine<int, string>();

        var result = await engine.RunAsync(
            initialState: 0,
            initialSignal: new FoldSignal<string>(FoldSignalKind.ExternalInput, "start", null),
            resolver: new ChainSignalResolver<int, string>(Chain),
            op: op,
            headProjector: new LastStateHeadProjector<int>(),
            cutoff: new BudgetCutoffPolicy<int>(),
            budget: FoldBudget.Unlimited,
            cts.Token);

        Assert.AreEqual(FoldCut.Cancelled, result.Cut);
        Assert.AreEqual(0, result.Coordinate);
        Assert.AreEqual(1, result.Trace.Count); // 仅初始点 s0
    }

    [TestMethod]
    public async Task Observer_ReceivesVertexAndStateAdvance()
    {
        var observer = new RecordingSignalObserver();
        var engine = new SignalDrivenFoldEngine<int, string>();

        var result = await engine.RunAsync(
            initialState: 0,
            initialSignal: new FoldSignal<string>(FoldSignalKind.ExternalInput, "start", null),
            resolver: new ChainSignalResolver<int, string>(Chain),
            op: new RecordingSignalOperator(),
            headProjector: new LastStateHeadProjector<int>(),
            cutoff: new BudgetCutoffPolicy<int>(),
            budget: new FoldBudget(2),
            observer: observer);

        Assert.AreEqual(2, observer.Vertices.Count);
        Assert.AreEqual(2, observer.StatesAdvanced.Count);
        Assert.AreEqual(FoldCut.BudgetExhausted, result.Cut);
    }
}
