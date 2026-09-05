using System.Collections.Generic;
using Agent.Core.TaskPool;
using Agent.State.Scheduling;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.AiScheduling;

namespace Agent.Core.Tests;

/// <summary>混合调度决策 → 主循环编排请求 纯映射单测（P2：表驱动锁死语义，终止对齐被动主循环）。</summary>
[TestClass]
public class HistoryDecisionMapperTests
{
    private static Dictionary<string, string> From(params (string K, string S)[] tasks)
    {
        var d = new Dictionary<string, string>();
        foreach (var (k, s) in tasks) d[k] = s;
        return d;
    }

    private static SchedulerDecision Decision(bool applied, SchedulerAction action, TransitionVerdict verdict,
        string? executor = null, string? intent = null, string? reason = null)
        => new(applied, action, verdict, TargetExecutorId: executor, Intent: intent, Reason: reason);

    [TestMethod]
    public void AdvanceRun_NotPaused_ActivatesTask()
    {
        var from = From(("task1", HistorySchedulerState.Status.Pending));
        var r = HistoryDecisionMapper.Map(Decision(true, SchedulerAction.AdvanceRun, TransitionVerdict.Pass, executor: "task1"), from);
        Assert.IsNotNull(r);
        Assert.AreEqual(TaskOrchestrationAction.ActivateTask, r.Action);
        Assert.AreEqual("task1", r.TaskKey);
    }

    [TestMethod]
    public void AdvanceRun_Paused_ResumesTask()
    {
        var from = From(("task1", HistorySchedulerState.Status.Paused));
        var r = HistoryDecisionMapper.Map(Decision(true, SchedulerAction.AdvanceRun, TransitionVerdict.Pass, executor: "task1"), from);
        Assert.AreEqual(TaskOrchestrationAction.ResumeTask, r!.Action);
    }

    [TestMethod]
    public void PauseRun_PausesTask()
    {
        var from = From(("task1", HistorySchedulerState.Status.Running));
        var r = HistoryDecisionMapper.Map(Decision(true, SchedulerAction.PauseRun, TransitionVerdict.Pass, executor: "task1"), from);
        Assert.AreEqual(TaskOrchestrationAction.PauseTask, r!.Action);
    }

    [TestMethod]
    public void Retry_ReactivesTask()
    {
        var from = From(("task1", HistorySchedulerState.Status.Paused));
        var r = HistoryDecisionMapper.Map(Decision(true, SchedulerAction.Retry, TransitionVerdict.Pass, executor: "task1"), from);
        Assert.AreEqual(TaskOrchestrationAction.ActivateTask, r!.Action);
    }

    [TestMethod]
    public void SwitchStrategy_CreatesTask_WithReasonAsInput()
    {
        var from = From(("task1", HistorySchedulerState.Status.Running));
        var r = HistoryDecisionMapper.Map(
            Decision(true, SchedulerAction.SwitchStrategy, TransitionVerdict.Pass, executor: "task2", reason: "转拆分方向"), from);
        Assert.IsNotNull(r);
        Assert.AreEqual(TaskOrchestrationAction.CreateTask, r.Action);
        Assert.AreEqual("task2", r.TaskKey);
        Assert.AreEqual("转拆分方向", r.Input);
    }

    [TestMethod]
    public void NotApplied_ReturnsNull()
        => Assert.IsNull(HistoryDecisionMapper.Map(
            Decision(false, SchedulerAction.AdvanceRun, TransitionVerdict.Pass, executor: "task1"),
            From(("task1", HistorySchedulerState.Status.Pending))));

    [TestMethod]
    public void Held_ReturnsNull()
        => Assert.IsNull(HistoryDecisionMapper.Map(
            Decision(true, SchedulerAction.AdvanceRun, TransitionVerdict.Held, executor: "task1"),
            From(("task1", HistorySchedulerState.Status.Pending))));

    [TestMethod]
    public void Reject_ReturnsNull()
        => Assert.IsNull(HistoryDecisionMapper.Map(
            Decision(true, SchedulerAction.AdvanceRun, TransitionVerdict.Reject, executor: "task1"),
            From(("task1", HistorySchedulerState.Status.Pending))));

    [TestMethod]
    public void Continue_ReturnsNull()
        => Assert.IsNull(HistoryDecisionMapper.Map(Decision(true, SchedulerAction.Continue, TransitionVerdict.Pass), From()));

    [TestMethod]
    public void Replan_And_Explore_ReturnNull()
    {
        var from = From(("task1", HistorySchedulerState.Status.Running));
        Assert.IsNull(HistoryDecisionMapper.Map(Decision(true, SchedulerAction.Replan, TransitionVerdict.Pass), from));
        Assert.IsNull(HistoryDecisionMapper.Map(Decision(true, SchedulerAction.Explore, TransitionVerdict.Pass), from));
    }

    [TestMethod]
    public void NullDecision_ReturnsNull()
        => Assert.IsNull(HistoryDecisionMapper.Map(null!, From()));
}
