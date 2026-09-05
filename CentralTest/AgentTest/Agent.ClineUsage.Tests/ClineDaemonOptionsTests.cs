using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.Modules.Pool;

namespace Agent.Modules.Tests
{
    /// <summary>
    /// ClineDaemonOptions 配置默认值测试：验证预算收敛/预热/看门狗相关默认值，防止默认值漂移。
    /// </summary>
    [TestClass]
    [TestCategory("Modules_Unit")]
    public class ClineDaemonOptionsTests
    {
        [TestMethod]
        public void 默认值_池大小3且启用预热且隔离数据目录()
        {
            var o = new ClineDaemonOptions();
            Assert.AreEqual(3, o.PoolSize, "默认 PoolSize=3");
            Assert.AreEqual(DataIsolation.Isolated, o.Isolation, "默认隔离数据目录策略（独立于 VSCode 的 ~/.cline，避免共享目录被动可见/污染；R1 决策收敛）");
            Assert.IsNull(o.DataDir, "默认 DataDir=null（Isolated 策略下由 daemon 池兜底为 RepositoryPaths.ResolveClineDaemonDataDir）");
            Assert.AreEqual(1, o.InfraRetryLimit, "默认基建故障重试上限=1");
        }

        [TestMethod]
        public void 默认值_IdleTimeout与WarmupTimeout与BudgetMargin非零()
        {
            var o = new ClineDaemonOptions();
            Assert.AreEqual(60, (int)(o.IdleTimeout ?? ClineDaemonOptions.DefaultIdleTimeout).TotalSeconds,
                "IdleTimeout 显式默认 60s（>10s 心跳，激活看门狗且长任务不被误杀）");
            Assert.AreEqual(180, (int)(o.WarmupTimeout ?? ClineDaemonOptions.DefaultWarmupTimeout).TotalSeconds,
                "WarmupTimeout 默认 180s（覆盖 56s 冷启动 + 波动；串行预热下首个 warm 约 60s，留足冷机/首次初始化余量）");
            Assert.AreEqual(5, (int)(o.BudgetMargin ?? ClineDaemonOptions.DefaultBudgetMargin).TotalSeconds,
                "BudgetMargin 默认 5s（预算收敛余量）");
        }

        [TestMethod]
        public void 自定义值_应透传()
        {
            var o = new ClineDaemonOptions(
                PoolSize: 2, Isolation: DataIsolation.Isolated, DataDir: @"D:\tmp\cline-data",
                InfraRetryLimit: 3);
            Assert.AreEqual(2, o.PoolSize, "自定义 PoolSize 应透传");
            Assert.AreEqual(DataIsolation.Isolated, o.Isolation, "自定义 Isolation 应透传");
            Assert.AreEqual(@"D:\tmp\cline-data", o.DataDir, "自定义 DataDir 应透传");
            Assert.AreEqual(3, o.InfraRetryLimit, "自定义 InfraRetryLimit 应透传");
        }
    }
}
