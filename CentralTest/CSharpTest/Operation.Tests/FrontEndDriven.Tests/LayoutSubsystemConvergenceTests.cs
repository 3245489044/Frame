#nullable enable

using FrontEndDriven.Container;
using FrontEndDriven.ControlRender.Layout;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrontEndDriven.Tests;

/// <summary>
/// LayoutSubsystem 宿主就绪门控（确定性收敛状态机）接线单测：
/// 验证测试态（重流依赖为空）下，宿主未挂载的 scope 进入 Blocked（不重排不空转）、
/// RemoveScope 清理收敛状态不残留。
/// 就绪分类判定本身（ClassifyHost 矩阵）由 LayoutConvergencePolicyTests 覆盖，本组只验接线。
/// </summary>
[TestClass]
public class LayoutSubsystemConvergenceTests
{
    /// <summary>测试态子系统：同步调度（dispatcher=null）+ 宿主解析返回 null（未挂载）。</summary>
    private static LayoutSubsystem CreateSubsystem()
    {
        var scheduler = new LayoutScheduler(() => null, _ => { });
        return new LayoutSubsystem(scheduler, () => null);
    }

    [TestMethod]
    public void ReflowNow_HostNotResolved_MarksBlocked()
    {
        // 宿主未挂载（hostMountResolver=null → host 解析失败）→ 门控置 Blocked，不重排不空转、不抛异常。
        var sub = CreateSubsystem();

        sub.ReflowNow("Content");

        Assert.AreEqual(LayoutConvergenceState.Blocked, sub.GetConvergenceState("Content"),
            "宿主未挂载的 scope 应进入 Blocked（等待挂载信号，不再重排空转）");
    }

    [TestMethod]
    public void ReflowNow_EmptyScope_NoState()
    {
        var sub = CreateSubsystem();

        sub.ReflowNow("");

        // 空 scope 短路返回，不产生收敛状态（未跟踪 = 视为 Converged）。
        Assert.AreEqual(LayoutConvergenceState.Converged, sub.GetConvergenceState(""));
    }

    [TestMethod]
    public void RemoveScope_ClearsConvergenceState()
    {
        var sub = CreateSubsystem();
        sub.ReflowNow("Content");
        Assert.AreEqual(LayoutConvergenceState.Blocked, sub.GetConvergenceState("Content"));

        sub.RemoveScope("Content");

        // 销毁路径清理收敛状态 → 未跟踪回退默认 Converged（不残留 Blocked 污染重载）。
        Assert.AreEqual(LayoutConvergenceState.Converged, sub.GetConvergenceState("Content"),
            "RemoveScope 后收敛状态应清除（回退默认），避免残留 Blocked 污染重载");
    }

    [TestMethod]
    public void Dispose_ClearsConvergenceState()
    {
        var sub = CreateSubsystem();
        sub.ReflowNow("A");
        sub.ReflowNow("B");

        sub.Dispose();

        Assert.AreEqual(LayoutConvergenceState.Converged, sub.GetConvergenceState("A"));
        Assert.AreEqual(LayoutConvergenceState.Converged, sub.GetConvergenceState("B"));
    }

    [TestMethod]
    public void ShouldRetry_UntrackedScope_True()
    {
        // 未跟踪 scope 视为可重试（预算耗尽仍重排消化，避免静默滞留——F6 修复目标不回退）。
        var sub = CreateSubsystem();
        Assert.IsTrue(sub.ShouldRetry("Unknown"));
    }

    [TestMethod]
    public void ShouldRetry_BlockedScope_False()
    {
        // Blocked scope 不重排（等待挂载信号），杜绝不可收敛自激。
        var sub = CreateSubsystem();
        sub.ReflowNow("Content"); // hostMountResolver=null → host 未挂载 → Blocked
        Assert.IsFalse(sub.ShouldRetry("Content"), "Blocked scope 不应重新调度");
    }
}