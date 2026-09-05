#nullable enable

using System;
using System.Collections.Generic;
using FrontEndDriven.ControlRender.Layout;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrontEndDriven.Tests;

/// <summary>
/// LayoutScheduler 反应式调度护栏单测：
/// 重排风暴（`_work` 内对同一 scope 反复 Schedule 重排自身）应在风暴阈值处中断，
/// 避免 ScheduleDrain 重入无限循环；正常 latest-wins 语义不回归。
/// </summary>
[TestClass]
public class LayoutSchedulerTests
{
    /// <summary>构造同步调度器（dispatcher=null → DrainAsync 在调用线程直接执行），工作委托记日志。</summary>
    private static LayoutScheduler CreateScheduler(List<string> workLog, Action<string>? onWork = null)
        => new(() => null, scope =>
        {
            workLog.Add(scope);
            onWork?.Invoke(scope);
        });

    [TestMethod]
    public void Schedule_NullOrEmptyScope_NoOp()
    {
        var workLog = new List<string>();
        var scheduler = CreateScheduler(workLog);

        scheduler.Schedule("");
        scheduler.Schedule(null!);

        Assert.AreEqual(0, workLog.Count, "空/空引用 scope 不应入队、不应消费");
    }

    [TestMethod]
    public void Schedule_DistinctScopes_AllConsumed()
    {
        var workLog = new List<string>();
        var scheduler = CreateScheduler(workLog);

        scheduler.Schedule("A");
        scheduler.Schedule("B");
        scheduler.Schedule("C");

        Assert.AreEqual(3, workLog.Count, "不同 scope 应各自消费一次");
    }

    [TestMethod]
    public void Schedule_ReflowStorm_SameScopeTerminatesViaGuard()
    {
        // 模拟 `_work` 内对同一 scope 反复 Schedule 重排自身的风暴：风暴护栏应在阈值处中断，
        // 而非无界重排（不产生无限循环 / ScheduleDrain 重入风暴）。
        var workLog = new List<string>();
        LayoutScheduler scheduler = null!;
        scheduler = CreateScheduler(workLog, scope => scheduler.Schedule(scope));

        scheduler.Schedule("A");

        // 护栏在阈值(16)+1 次连续重排处中断；此处断言有界（既实际发生了多次重排、又未失控）。
        Assert.IsTrue(workLog.Count > 1, "风暴应实际发生多次重排");
        Assert.IsTrue(workLog.Count < 50, "风暴护栏应中断重排，避免无限循环");
        Assert.IsTrue(workLog.TrueForAll(s => s == "A"), "重排范围仅限风暴 scope");
    }
}
