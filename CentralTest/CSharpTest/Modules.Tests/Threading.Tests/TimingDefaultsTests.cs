using Modules.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Modules.Threading.Tests;

[TestClass]
public class TimingDefaultsTests
{
    [TestMethod]
    public void ClineWarmup_CoversDocumentedColdStart()
    {
        // 文档实测 ClineCore 冷启动 ≈ 56s；warmup 预算须显著大于该值。
        Assert.IsTrue(TimingDefaults.ClineWarmup > TimeSpan.FromSeconds(56));
        Assert.AreEqual(TimeSpan.FromSeconds(120), TimingDefaults.ClineWarmup);
    }

    [TestMethod]
    public void PipelineStage_MatchesLegacyNodeDefault()
    {
        // 对齐 WorkflowExecution.Node / Execution.cs 的 30000ms 单一来源。
        Assert.AreEqual(TimeSpan.FromSeconds(30), TimingDefaults.PipelineStage);
    }

    [TestMethod]
    public void AefIdleWindow_Defaults_ArePositive()
    {
        Assert.IsTrue(TimingDefaults.AefIdleWindow > TimeSpan.Zero);
        Assert.IsTrue(TimingDefaults.AefPollInterval > TimeSpan.Zero);
    }
}
