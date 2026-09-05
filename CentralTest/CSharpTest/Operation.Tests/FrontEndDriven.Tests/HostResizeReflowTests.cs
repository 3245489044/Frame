#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using FrontEndDriven.ControlRender.Layout;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrontEndDriven.Tests;

/// <summary>
/// 反应式布局子系统（Phase 1）单测：宿主容器拉伸 → 布局重排调度 的订阅/退订/幂等语义。
/// 重流依赖（HostBasisSystem/LayoutSystem/Collector/OutputPort）不在单测态注入，RecomputeAndProject 短路——
/// 本组用例只验证「宿主 Resize 触发调度、RemoveScope 停止调度」的接线正确性。
/// </summary>
[TestClass]
public class HostResizeReflowTests
{
    /// <summary>构造测试态子系统：同步调度（dispatcher=null），工作委托记录 scope 到日志列表。</summary>
    private static LayoutSubsystem CreateSubsystem(List<string> workLog)
    {
        var scheduler = new LayoutScheduler(() => null, workLog.Add);
        return new LayoutSubsystem(scheduler, () => null);
    }

    [TestMethod]
    public void RegisterScope_ResizeHost_SchedulesReflow()
    {
        var workLog = new List<string>();
        var sub = CreateSubsystem(workLog);
        var panel = new Panel { Name = "content", Size = new Size(100, 100) };

        sub.RegisterScope("Content", panel);
        panel.Size = new Size(200, 100);

        Assert.AreEqual(1, workLog.Count, "宿主拉伸应触发一次布局重排调度");
        Assert.AreEqual("Content", workLog[0]);
    }

    [TestMethod]
    public void RegisterScope_Idempotent_SameHost_SingleSubscription()
    {
        var workLog = new List<string>();
        var sub = CreateSubsystem(workLog);
        var panel = new Panel { Size = new Size(100, 100) };

        sub.RegisterScope("Content", panel);
        sub.RegisterScope("Content", panel); // 幂等：同 scope 同 host 不重复订阅
        panel.Size = new Size(150, 100);

        Assert.AreEqual(1, workLog.Count, "同 scope 同 host 重复注册不应重复订阅（一次拉伸仅一次调度）");
    }

    [TestMethod]
    public void RemoveScope_StopsReflow()
    {
        var workLog = new List<string>();
        var sub = CreateSubsystem(workLog);
        var panel = new Panel { Size = new Size(100, 100) };

        sub.RegisterScope("Content", panel);
        sub.RemoveScope("Content");
        panel.Size = new Size(200, 100);

        Assert.AreEqual(0, workLog.Count, "RemoveScope 后宿主拉伸不应再触发重排调度");
    }

    [TestMethod]
    public void RegisterScope_FormHost_NotSkipped()
    {
        var workLog = new List<string>();
        var sub = CreateSubsystem(workLog);
        var form = new Form { Size = new Size(300, 200) };

        sub.RegisterScope("Content", form);
        form.Size = new Size(400, 300);

        Assert.AreEqual(1, workLog.Count, "Step2：Form 宿主不再跳过——随缩放同样触发反应式重排（根除 Form 直挂 scope 不自动刷新）");
    }

    [TestMethod]
    public void RegisterScope_NullOrEmptyScope_NoOp()
    {
        var workLog = new List<string>();
        var sub = CreateSubsystem(workLog);
        var panel = new Panel { Size = new Size(100, 100) };

        sub.RegisterScope("", panel);
        sub.RegisterScope(null!, panel);
        panel.Size = new Size(200, 100);

        Assert.AreEqual(0, workLog.Count, "空/空引用 scope 不应注册（不订阅、不调度）");
    }

    [TestMethod]
    public void ReflowNow_NullOrEmptyScope_NoOp()
    {
        var sub = CreateSubsystem(new List<string>());

        sub.ReflowNow("");
        sub.ReflowNow(null!);

        // 不抛异常即通过（空 scope 短路返回）
    }

    [TestMethod]
    public void ReflowNow_TestCtor_ShortCircuitsWithoutReflowDeps()
    {
        // 测试态构造（HostBasisSystem/LayoutSystem/LayoutProjector 为空）→ 同步重排短路返回，不抛异常。
        var sub = CreateSubsystem(new List<string>());

        sub.ReflowNow("Content");
    }
}
