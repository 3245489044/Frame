using System;
using System.Threading;
using System.Threading.Tasks;
using BasicMethod.Calculus;
using BasicMethod.Calculus.Convergers;
using BasicMethod.Calculus.Evolvers;
using BasicMethod.Calculus.Extensions;
using BasicMethod.Calculus.Integrators;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.LoopFoldMethod.Adapters;
using Operation.LoopFoldMethod.Cutoff;
using Operation.LoopFoldMethod.Fold;
using Operation.LoopFoldMethod.FoldHead;
using Operation.LoopFoldMethod.Operator;

namespace Operation.Tests.LoopFoldMethod;

/// <summary>
/// 算子层复用适配器（Adapters/）测试：用 BasicMethod.Calculus 内置算子组合验证折叠全分支
/// （收敛 / 预算熔断 / 超时 / 重试上限 / 前缀折叠 / 取消 / 双回归）。全部零 AI 额度、纯 CI 可跑。
/// 边界诚实声明：ConvergenceCutoffPolicy（谓词包装 IConverger）只映射 HasConverged 到判定，Converge 的「朝不动点变换」与 attempts 计数不混入截断。
/// </summary>
[TestClass]
public class AdaptersTests
{
    // ---------- EvolverFoldOperator：演化即展开 ----------

    [TestMethod]
    public async Task EvolverFoldOperator_ExpandsIncrementally()
    {
        // PipelineEvolver：state -> state+1；顶点由 vertexFactory 从下一状态合成（x => x）。
        var evolver = new PipelineEvolver<int>("inc", s => s + 1);
        var op = new EvolverFoldOperator<int, int>(evolver, s => s);

        var engine = new FoldEngine<int, int>();
        var result = await engine.RunAsync(
            initialState: 0,
            op: op,
            headProjector: new LastStateHeadProjector<int>(),
            cutoff: new BudgetCutoffPolicy<int>(),
            budget: new FoldBudget(3));

        Assert.AreEqual(FoldCut.BudgetExhausted, result.Cut);
        Assert.AreEqual(3, result.Coordinate);
        Assert.AreEqual(3, result.Vertices.Count);
        Assert.AreEqual(4, result.Trace.Count);   // s0..s3
        Assert.AreEqual(3, result.Head);          // LastState = 3
    }

    // ---------- 收敛截断（ConvergenceCutoffPolicy 谓词包装 IConverger） ----------

    [TestMethod]
    public async Task ConvergerCutoffPolicy_StableConverger_ConvergesOnStableHead()
    {
        // 恒等演化（state 不推进）：第一轮后 head 稳定 → StableConverger 判收敛成功。
        var evolver = new PipelineEvolver<int>("id", s => s);
        var op = new EvolverFoldOperator<int, int>(evolver, s => s);
        var converger = new StableConverger<int>("stable");

        var engine = new FoldEngine<int, int>();
        var result = await engine.RunAsync(
            initialState: 0,
            op: op,
            headProjector: new LastStateHeadProjector<int>(),
            cutoff: Convergence(converger),
            budget: FoldBudget.Unlimited);

        Assert.AreEqual(FoldCut.Success, result.Cut);
        Assert.AreEqual(1, result.Coordinate);
        Assert.AreEqual(1, result.Vertices.Count);
        Assert.AreEqual(2, result.Trace.Count);   // s0, s0
        Assert.AreEqual(0, result.Head);
    }

    // ---------- RetryConverger：状态稳定即收敛 / 非收敛受预算约束（Converge 不混入截断） ----------

    [TestMethod]
    public async Task RetryConverger_StableState_Converges()
    {
        // 恒等演化 + RetryConverger：current == previous → HasConverged true → Success。
        var evolver = new PipelineEvolver<int>("id", s => s);
        var op = new EvolverFoldOperator<int, int>(evolver, s => s);
        var converger = new RetryConverger<int>("retry", s => (false, s), maxRetries: 2);

        var engine = new FoldEngine<int, int>();
        var result = await engine.RunAsync(
            initialState: 0,
            op: op,
            headProjector: new LastStateHeadProjector<int>(),
            cutoff: Convergence(converger),
            budget: FoldBudget.Unlimited);

        Assert.AreEqual(FoldCut.Success, result.Cut);
        Assert.AreEqual(1, result.Coordinate);
    }

    [TestMethod]
    public async Task RetryConverger_NonConvergent_BudgetBounded()
    {
        // 递增演化（永不等 previous）+ RetryConverger：适配器不调用 Converge，attempts 恒 0，永不收敛
        // → 预算熔断兜底（验证「Converge 变换与 attempts 计数不混入截断执法」的诚实边界）。
        var evolver = new PipelineEvolver<int>("inc", s => s + 1);
        var op = new EvolverFoldOperator<int, int>(evolver, s => s);
        var converger = new RetryConverger<int>("retry", s => (false, s), maxRetries: 2);

        var engine = new FoldEngine<int, int>();
        var result = await engine.RunAsync(
            initialState: 0,
            op: op,
            headProjector: new LastStateHeadProjector<int>(),
            cutoff: Convergence(converger),
            budget: new FoldBudget(3));

        Assert.AreEqual(FoldCut.BudgetExhausted, result.Cut);
        Assert.AreEqual(3, result.Coordinate);
        Assert.AreEqual(3, result.Head);
    }

    // ---------- TimeoutConverger：超时收敛 ----------

    [TestMethod]
    public async Task TimeoutConverger_TimeoutZero_Converges()
    {
        // TimeoutConverger(timeout: Zero)：首次 HasConverged 即因超时窗口已满返回 true → Success。
        var evolver = new PipelineEvolver<int>("inc", s => s + 1);
        var op = new EvolverFoldOperator<int, int>(evolver, s => s);
        var converger = new TimeoutConverger<int>("timeout", (c, p) => false, TimeSpan.Zero);

        var engine = new FoldEngine<int, int>();
        var result = await engine.RunAsync(
            initialState: 0,
            op: op,
            headProjector: new LastStateHeadProjector<int>(),
            cutoff: Convergence(converger),
            budget: FoldBudget.Unlimited);

        Assert.AreEqual(FoldCut.Success, result.Cut);
        Assert.AreEqual(1, result.Coordinate);
    }

    // ---------- IntegratorFoldHead：前缀折叠成判定点 ----------

    [TestMethod]
    public void IntegratorFoldHead_AccumulatesTraceSum()
    {
        // MergeIntegrator<int>（求和）：Head(trace) = fold(trace, Reset=0, Integrate=+)
        var integrator = new MergeIntegrator<int>("sum", () => 0, (a, b) => a + b);
        var head = new IntegratorFoldHead<int>(integrator);

        var result = head.Head(new[] { 1, 2, 3 });
        Assert.AreEqual(6, result);
    }

    // ---------- 取消：适配器层取消令牌 ----------

    [TestMethod]
    public void EvolverFoldOperator_CancelledToken_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var op = new EvolverFoldOperator<int, int>(new PipelineEvolver<int>("inc", s => s + 1), s => s);

        Assert.ThrowsException<OperationCanceledException>(
            () => op.Expand(0, new FoldContext<int>(0, FoldBudget.Unlimited, Array.Empty<FoldVertex<int>>()), cts.Token));
    }

    // ---------- Task 4 · 双回归：同一演化序列经适配器后引擎判定一致（基准 = SnapshotDrivenLoopEngine 行为） ----------

    [TestMethod]
    public async Task DoubleRegression_NonConvergent_MatchesEvolveUntilConverged()
    {
        // 递增演化 + StableConverger（不收敛）：同步 EvolveUntilConverged(max:5) 与 FoldEngine(budget:5) 最终状态一致。
        var evolver = new PipelineEvolver<int>("inc", s => s + 1);
        var converger = new StableConverger<int>("stable");

        int syncFinal = evolver.EvolveUntilConverged(converger, initial: 0, maxIterations: 5);
        Assert.AreEqual(5, syncFinal); // 5 步演化，均未收敛，current=5

        var op = new EvolverFoldOperator<int, int>(evolver, s => s);
        var engine = new FoldEngine<int, int>();
        var result = await engine.RunAsync(
            initialState: 0,
            op: op,
            headProjector: new LastStateHeadProjector<int>(),
            cutoff: Convergence(converger),
            budget: new FoldBudget(5));

        Assert.AreEqual(syncFinal, result.Head); // 同一演化序列，折叠框架判定与基准一致
        Assert.AreEqual(FoldCut.BudgetExhausted, result.Cut);
    }

    [TestMethod]
    public async Task DoubleRegression_Convergent_MatchesEvolveUntilConverged()
    {
        // 恒等演化 + StableConverger（第一轮即收敛）：两端均得 0。
        var evolver = new PipelineEvolver<int>("id", s => s);
        var converger = new StableConverger<int>("stable");

        int syncFinal = evolver.EvolveUntilConverged(converger, initial: 0, maxIterations: 100);
        Assert.AreEqual(0, syncFinal);

        var op = new EvolverFoldOperator<int, int>(evolver, s => s);
        var engine = new FoldEngine<int, int>();
        var result = await engine.RunAsync(
            initialState: 0,
            op: op,
            headProjector: new LastStateHeadProjector<int>(),
            cutoff: Convergence(converger),
            budget: FoldBudget.Unlimited);

        Assert.AreEqual(FoldCut.Success, result.Cut);
        Assert.AreEqual(syncFinal, result.Head);
    }

    /// <summary>把 <see cref="IConverger{T}"/> 的 HasConverged 收敛语义包装为统一收敛截断策略
    /// （<see cref="ConvergenceCutoffPolicy{TState}"/>）；Converge 变换不混入截断（诚实边界）。</summary>
    private static ConvergenceCutoffPolicy<int> Convergence(IConverger<int> converger)
        => new((a, b) => converger.HasConverged(a, b));
}

