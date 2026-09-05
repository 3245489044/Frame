using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Agent.Core.TaskPool;
using Agent.Session;
using Agent.State.Scheduling;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.AiScheduling;
using Share.Conversation;

namespace Agent.Core.Tests;

/// <summary>历史主循环决策器单测（P2：组合 AiSchedulerStateMachine = FakeDirector + FakeGuard，零 LLM 不触网）。</summary>
[TestClass]
public class HistoryLoopDeciderTests
{
    private static HistorySchedulerState NewState(params (string Task, string Status)[] tasks)
    {
        var s = new HistorySchedulerState { AvailableStrategyIds = { "default", "split" } };
        foreach (var (t, st) in tasks) s.AgentStatus[t] = st;
        return s;
    }

    private static StandardContext EmptyView() => StandardContext.Empty(StandardScope.History, "h1", null);

    private static StateTransitionProposal Advance(string task)
        => new(SchedulerAction.AdvanceRun, TargetExecutorId: task, TargetRunStatusId: HistorySchedulerState.Status.Running, Confidence: 1.0);

    [TestMethod]
    public async Task NextAsync_AppliesProposal_ReturnsMappedRequest()
    {
        var decider = new HistoryLoopDecider(
            new FakeHistoryStateDirector { Proposal = Advance("task1") },
            new FakeHistoryTransitionGuard { Verdict = TransitionVerdict.Pass },
            NewState(("task1", HistorySchedulerState.Status.Pending)));

        var req = await decider.NextAsync(EmptyView(), enactedCount: 0);

        Assert.IsNotNull(req);
        Assert.AreEqual(TaskOrchestrationAction.ActivateTask, req.Action);
        Assert.AreEqual("task1", req.TaskKey);
    }

    [TestMethod]
    public async Task NextAsync_DirectorNull_ReturnsNull()
    {
        var decider = new HistoryLoopDecider(
            new FakeHistoryStateDirector(), // 无提议 → stay/continue（fail-safe）
            new FakeHistoryTransitionGuard(),
            NewState(("task1", HistorySchedulerState.Status.Pending)));

        Assert.IsNull(await decider.NextAsync(EmptyView(), 0));
    }

    [TestMethod]
    public async Task NextAsync_GuardReject_ReturnsNull()
    {
        var decider = new HistoryLoopDecider(
            new FakeHistoryStateDirector { Proposal = Advance("task1") },
            new FakeHistoryTransitionGuard { Verdict = TransitionVerdict.Reject },
            NewState(("task1", HistorySchedulerState.Status.Pending)));

        Assert.IsNull(await decider.NextAsync(EmptyView(), 0));
    }

    [TestMethod]
    public async Task NextAsync_ForwardsHistoryContext_ToDirector()
    {
        var director = new FakeHistoryStateDirector { Proposal = Advance("task1") };
        var decider = new HistoryLoopDecider(
            director,
            new FakeHistoryTransitionGuard { Verdict = TransitionVerdict.Pass },
            NewState(("task1", HistorySchedulerState.Status.Pending)));

        var view = new StandardContext(StandardScope.History, "h1", null,
            new List<ConversationMessage> { new(ConversationRole.User, null, null, "hello", 1, default) });

        await decider.NextAsync(view, enactedCount: 0);

        Assert.IsNotNull(director.LastContext);
        Assert.AreEqual("1条(User:hello)", director.LastContext!.HistoryContext);
    }

    [TestMethod]
    public void OnEnacted_SyncsShadowLifecycle()
    {
        var decider = new HistoryLoopDecider(
            new FakeHistoryStateDirector(),
            new FakeHistoryTransitionGuard(),
            NewState());

        decider.OnEnacted(new TaskOrchestrationRequest(TaskOrchestrationAction.CreateTask, "task1"));
        Assert.AreEqual(HistorySchedulerState.Status.Pending, decider.State.AgentStatus["task1"]);

        decider.OnEnacted(new TaskOrchestrationRequest(TaskOrchestrationAction.ActivateTask, "task1"));
        Assert.AreEqual(HistorySchedulerState.Status.Running, decider.State.AgentStatus["task1"]);

        decider.OnEnacted(new TaskOrchestrationRequest(TaskOrchestrationAction.PauseTask, "task1"));
        Assert.AreEqual(HistorySchedulerState.Status.Paused, decider.State.AgentStatus["task1"]);

        decider.OnEnacted(new TaskOrchestrationRequest(TaskOrchestrationAction.ResumeTask, "task1"));
        Assert.AreEqual(HistorySchedulerState.Status.Running, decider.State.AgentStatus["task1"]);

        decider.OnEnacted(new TaskOrchestrationRequest(TaskOrchestrationAction.TerminateTask, "task1"));
        Assert.AreEqual(HistorySchedulerState.Status.Done, decider.State.AgentStatus["task1"]);
    }
}


// ---- 历史主循环决策器测试用内存假端口（FakeDirector / FakeGuard）----

internal sealed class FakeHistoryStateDirector : IStateDirector
{
    /// <summary>固定返回的提议；null = 无提议（stay，fail-safe）。</summary>
    public StateTransitionProposal? Proposal;

    /// <summary>最近一次决策接收到的导演上下文（供断言 history 视图注入）。</summary>
    public SchedulerDirectorContext? LastContext;

    public Task<StateTransitionProposal?> DecideAsync(SchedulerDirectorContext context, CancellationToken ct = default)
    {
        LastContext = context;
        return Task.FromResult(Proposal);
    }
}

internal sealed class FakeHistoryTransitionGuard : ITransitionGuard
{
    /// <summary>固定返回的裁决（默认 Pass）。</summary>
    public TransitionVerdict Verdict = TransitionVerdict.Pass;

    public TransitionVerdict Verify(StateTransitionProposal proposal, SchedulerDirectorContext context)
        => Verdict;
}
