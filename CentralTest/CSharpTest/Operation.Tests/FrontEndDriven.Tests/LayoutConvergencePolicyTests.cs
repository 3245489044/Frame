#nullable enable

using FrontEndDriven.ControlRender.Layout;
using FrontEndDriven.Shared.Framework;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrontEndDriven.Tests;

/// <summary>
/// LayoutConvergencePolicy 单测：四态分类矩阵 / 退避序列 / 放弃阈值 / 阈值单源不变量。
/// 纯函数，无副作用，是确定性收敛状态机的数据与判定层。
/// </summary>
[TestClass]
public class LayoutConvergencePolicyTests
{
    [TestMethod]
    public void ClassifyHost_HostNotResolved_Blocked()
    {
        var r = LayoutConvergencePolicy.ClassifyHost(
            hostResolved: false,
            clientReady: true, parentClientReady: true, rootFound: true);
        Assert.AreEqual(LayoutHostReadiness.Blocked, r, "宿主未解析应等待挂载信号，不重试不空转");
    }

    [TestMethod]
    public void ClassifyHost_FormClientReady_Ready()
    {
        // Step2：Form 宿主与容器宿主一视同仁——client 就绪即 Ready（可随缩放/移动/DPI 反应式重排）。
        // Form 的 Parent 恒为 null → parentClientReady=false，仅凭自身 ClientSize 判定。
        var r = LayoutConvergencePolicy.ClassifyHost(
            hostResolved: true,
            clientReady: true, parentClientReady: false, rootFound: true);
        Assert.AreEqual(LayoutHostReadiness.Ready, r, "Form 客户区就绪 → 允许反应式重排");
    }

    [TestMethod]
    public void ClassifyHost_FormClientAndParentZero_Retrying()
    {
        // Step2：Form 自身 0x0（未显示）且无父面板 → 瞬态未就绪，有限退避重试（不再一票 Blocked）。
        var r = LayoutConvergencePolicy.ClassifyHost(
            hostResolved: true,
            clientReady: false, parentClientReady: false, rootFound: true);
        Assert.AreEqual(LayoutHostReadiness.Retrying, r, "Form 未显示 0x0 → 瞬态退避重试");
    }

    [TestMethod]
    public void ClassifyHost_ClientAndParentZero_Retrying()
    {
        var r = LayoutConvergencePolicy.ClassifyHost(
            hostResolved: true,
            clientReady: false, parentClientReady: false, rootFound: true);
        Assert.AreEqual(LayoutHostReadiness.Retrying, r, "宿主已挂载但尺寸瞬态 0x0 → 有限退避重试");
    }

    [TestMethod]
    public void ClassifyHost_ParentClientReady_SingleZeroNotBlocked()
    {
        // 仅宿主自身 0x0 但父面板有效 → 可经父面板回退取得基准，属瞬态（Retrying 而非 Blocked）。
        var r = LayoutConvergencePolicy.ClassifyHost(
            hostResolved: true,
            clientReady: false, parentClientReady: true, rootFound: true);
        Assert.AreEqual(LayoutHostReadiness.Ready, r, "父面板尺寸可回退 → 基准可达");
    }

    [TestMethod]
    public void ClassifyHost_RootNotFound_Retrying()
    {
        var r = LayoutConvergencePolicy.ClassifyHost(
            hostResolved: true,
            clientReady: true, parentClientReady: true, rootFound: false);
        Assert.AreEqual(LayoutHostReadiness.Retrying, r, "root 暂未找到 → 瞬态退避重试");
    }

    [TestMethod]
    public void ClassifyHost_AllReady_Ready()
    {
        var r = LayoutConvergencePolicy.ClassifyHost(
            hostResolved: true,
            clientReady: true, parentClientReady: true, rootFound: true);
        Assert.AreEqual(LayoutHostReadiness.Ready, r, "全就绪 → 允许重排");
    }

    [TestMethod]
    public void NextBackoff_Sequence_1_2_4_ThenNull()
    {
        Assert.AreEqual(1, LayoutConvergencePolicy.NextBackoff(0));
        Assert.AreEqual(2, LayoutConvergencePolicy.NextBackoff(1));
        Assert.AreEqual(4, LayoutConvergencePolicy.NextBackoff(2));
        Assert.IsNull(LayoutConvergencePolicy.NextBackoff(3), "超出退避预算 → null（转 Abandoned）");
        Assert.IsNull(LayoutConvergencePolicy.NextBackoff(-1), "非法 attempt → null");
    }

    [TestMethod]
    public void ShouldAbandon_Threshold_3()
    {
        Assert.IsFalse(LayoutConvergencePolicy.ShouldAbandon(0));
        Assert.IsFalse(LayoutConvergencePolicy.ShouldAbandon(2));
        Assert.IsTrue(LayoutConvergencePolicy.ShouldAbandon(3));
        Assert.IsTrue(LayoutConvergencePolicy.ShouldAbandon(5));
    }

    [TestMethod]
    public void RetryBudget_Matches_Backoff_Length()
    {
        // RetryBudget 与退避序列长度一致：耗尽退避预算即放弃，杜绝无界自激。
        Assert.AreEqual(LayoutConvergencePolicy.RetryBudget, LayoutConvergencePolicy.RetryBackoff.Length);
    }

    [TestMethod]
    public void Thresholds_Converged_SingleSource()
    {
        // 阈值单源不变量：策略集中值即为各子系统实际引用值（防魔数再次漂移回各实现）。
        Assert.AreEqual(16, LayoutConvergencePolicy.MaxLayoutPasses);
        Assert.AreEqual(8, LayoutConvergencePolicy.MaxProjectionPasses);
        Assert.AreEqual(16, LayoutConvergencePolicy.ReflowStormThreshold);
        Assert.AreEqual(3, LayoutConvergencePolicy.RetryBudget);
        Assert.AreEqual(3, LayoutConvergencePolicy.AbandonThreshold);
        // 排空预算已归入共享队列策略（LayoutScheduler / RenderRequestQueue 单点可调）。
        Assert.AreEqual(32, QueueDrainPolicy.MaxPassesPerDrain);
    }
}
