using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.LoopFoldMethod.Attempt;
using Operation.LoopFoldMethod.Decision;
using Operation.LoopFoldMethod.Fold;
using Operation.LoopFoldMethod.FoldHead;
using Operation.LoopFoldMethod.Operator;
using Operation.LoopFoldMethod.Signal;

namespace Operation.Tests.LoopFoldMethod;

/// <summary>
/// DecisionFoldEngine 决策折叠分层测试：LLM skill 产出折叠链标识符 D′ 链推进 / 链终点收敛 /
/// 确定性硬终止先于 skill 拦截（skill 不被调用）。验证「决策 = 产出折叠链标识符或终止信号」的统一语义，
/// 与 SEP.DeterministicCoreDisjoint（硬性引擎内、skill 不触碰确定性核心）。全部零 AI，纯 CI 可跑。
/// </summary>
[TestClass]
public class DecisionFoldEngineTests
{
    [TestMethod]
    public async Task SkillAdvancesChain_ThenTerminal_Converges()
    {
        // LLM skill 产出 D′ 链：state0→collect, state1→analyze, state2→act, state3→Stop(Success)。
        // 确定性硬终止永不命中。→ 链推进到链终点收敛成功。
        var script = new ScriptedDecisionSkill(new Dictionary<int, (string? Next, TerminationSignal? Term)>
        {
            [0] = ("collect", null),
            [1] = ("analyze", null),
            [2] = ("act", null),
            [3] = (null, TerminationSignal.Success),
        });
        var termination = new ScriptedTermination();
        var engine = new DecisionFoldEngine<int, string>();

        var result = await engine.RunAsync(
            initialState: 0,
            initialSignal: new FoldSignal<string>(FoldSignalKind.ExternalInput, "start", null),
            deterministic: termination,
            skill: script,
            headProjector: new LastStateHeadProjector<int>(),
            budget: FoldBudget.Unlimited);

        Assert.AreEqual(FoldCut.Success, result.Cut);
        Assert.AreEqual(3, result.Coordinate);       // 3 步 ContinueTo + 1 步 Terminate
        Assert.AreEqual(3, result.Vertices.Count);
        Assert.AreEqual(4, script.DecidedStates.Count); // state 0..3
        CollectionAssert.AreEqual(new[] { "collect", "analyze", "act" },
            script.OutcomeKeys.Where(o => o is not null).Cast<string>().ToArray());
    }

    [TestMethod]
    public async Task HardTermination_InterceptsBeforeSkill_Stops()
    {
        // 确定性硬终止在 state=1 命中 → 直接终止（TripTerminated），skill 在命中轮不被调用。
        var skill = new ScriptedDecisionSkill(new Dictionary<int, (string? Next, TerminationSignal? Term)>
        {
            [0] = ("collect", null),
            [1] = ("analyze", null),
        });
        var termination = new ScriptedTermination(tripAtState: 1, TerminationSignal.TripTerminated);
        var engine = new DecisionFoldEngine<int, string>();

        var result = await engine.RunAsync(
            initialState: 0,
            initialSignal: new FoldSignal<string>(FoldSignalKind.ExternalInput, "start", null),
            deterministic: termination,
            skill: skill,
            headProjector: new LastStateHeadProjector<int>(),
            budget: FoldBudget.Unlimited);

        // 硬终止 → FoldCut.TripTerminated；skill 只在 state0 被调（state1 被硬终止拦截）。
        Assert.AreEqual(FoldCut.TripTerminated, result.Cut);
        Assert.AreEqual(1, result.Coordinate);
        CollectionAssert.AreEqual(new[] { 0 }, skill.DecidedStates);
        CollectionAssert.AreEqual(new[] { 0, 1 }, termination.CheckedStates);
    }

    [TestMethod]
    public async Task HardTimeout_MapsToBudgetExhausted()
    {
        var skill = new ScriptedDecisionSkill(new Dictionary<int, (string? Next, TerminationSignal? Term)>
        {
            [0] = ("collect", null),
        });
        var termination = new ScriptedTermination(tripAtState: 1, TerminationSignal.Timeout);
        var engine = new DecisionFoldEngine<int, string>();

        var result = await engine.RunAsync(
            initialState: 0,
            initialSignal: new FoldSignal<string>(FoldSignalKind.ExternalInput, "start", null),
            deterministic: termination,
            skill: skill,
            headProjector: new LastStateHeadProjector<int>(),
            budget: FoldBudget.Unlimited);

        Assert.AreEqual(FoldCut.BudgetExhausted, result.Cut);
        Assert.AreEqual(1, result.Coordinate);
    }

    [TestMethod]
    public async Task Budget_ExhaustedWithoutSkillTerminal_Stops()
    {
        // skill 永不终止（一直 ContinueTo），预算坐标耗尽 → 引擎内 Timeout（预算终止，不经 skill 终态）。
        var skill = new ScriptedDecisionSkill(new Dictionary<int, (string? Next, TerminationSignal? Term)>
        {
            [0] = ("collect", null),
            [1] = ("analyze", null),
            [2] = ("act", null),
            [3] = ("more", null),
        });
        var termination = new ScriptedTermination();
        var engine = new DecisionFoldEngine<int, string>();

        var result = await engine.RunAsync(
            initialState: 0,
            initialSignal: new FoldSignal<string>(FoldSignalKind.ExternalInput, "start", null),
            deterministic: termination,
            skill: skill,
            headProjector: new LastStateHeadProjector<int>(),
            budget: new FoldBudget(2));

        Assert.AreEqual(FoldCut.BudgetExhausted, result.Cut);
        Assert.AreEqual(2, result.Coordinate);
    }

    private sealed class ScriptedDecisionSkill : IDecisionSkill<int, string>
    {
        private readonly IReadOnlyDictionary<int, (string? Next, TerminationSignal? Term)> _script;
        public List<int> DecidedStates { get; } = new();
        public List<string?> OutcomeKeys { get; } = new();

        public ScriptedDecisionSkill(IReadOnlyDictionary<int, (string? Next, TerminationSignal? Term)> script)
            => _script = script;

        public Task<DecisionStep<int, string>> DecideAsync(
            int state, FoldSignal<string> signal, FoldContext<string> ctx, CancellationToken ct)
        {
            DecidedStates.Add(state);
            var (next, term) = _script[state];
            if (next is not null)
            {
                OutcomeKeys.Add(next);
                return Task.FromResult(new DecisionStep<int, string>(state + 1, $"out:{next}", DecisionOutcome.Next(next)));
            }
            OutcomeKeys.Add(null);
            return Task.FromResult(new DecisionStep<int, string>(state, $"done:{state}", DecisionOutcome.Stop(term ?? TerminationSignal.Success)));
        }
    }

    private sealed class ScriptedTermination : IDeterministicTermination<int>
    {
        private readonly int _tripAtState;
        private readonly TerminationSignal? _signal;
        public List<int> CheckedStates { get; } = new();

        public ScriptedTermination(int? tripAtState = null, TerminationSignal? signal = null)
        {
            _tripAtState = tripAtState ?? int.MaxValue;
            _signal = signal;
        }

        public TerminationSignal? Check(int state)
        {
            CheckedStates.Add(state);
            return state >= _tripAtState ? (_signal ?? TerminationSignal.TripTerminated) : null;
        }
    }
}
