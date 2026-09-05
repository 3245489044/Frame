using System.Collections.Generic;
using Agent.Axioms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkFlowMethod.LoopFramework.Axioms;

namespace Agent.Core.Tests;

[TestClass]
public class LoopConfigHotReloadTests
{
    [TestMethod]
    public void Apply_PresentRowsOverrideDefaults()
    {
        var cfg = new AgentLoopConfig();
        var rows = new Dictionary<string, string>
        {
            [LoopConfigHotReload.KeyMaxProgress] = "7",
            [LoopConfigHotReload.KeyRequestTimeoutSeconds] = "90",
            [LoopConfigHotReload.KeyRetryLimit] = "3",
            [LoopConfigHotReload.KeyLoopScope] = "procedure",
        };

        LoopConfigHotReload.Apply(cfg, rows);

        Assert.AreEqual(7, cfg.MaxProgress);
        Assert.AreEqual(System.TimeSpan.FromSeconds(90), cfg.RequestTimeout);
        Assert.AreEqual(3, cfg.RetryLimit);
        Assert.AreEqual(Scope.Procedure, cfg.LoopScope);
    }

    [TestMethod]
    public void Apply_AbsentKeysKeepCurrentValues()
    {
        var cfg = new AgentLoopConfig { MaxProgress = 5, RequestTimeout = System.TimeSpan.FromMinutes(1), RetryLimit = 2 };

        LoopConfigHotReload.Apply(cfg, new Dictionary<string, string> { [LoopConfigHotReload.KeyMaxProgress] = "9" });

        Assert.AreEqual(9, cfg.MaxProgress);                 // 存在的键覆盖
        Assert.AreEqual(System.TimeSpan.FromMinutes(1), cfg.RequestTimeout); // 缺省键保持
        Assert.AreEqual(2, cfg.RetryLimit);
    }

    [TestMethod]
    public void Apply_InvalidValuesIgnored()
    {
        var cfg = new AgentLoopConfig();
        var rows = new Dictionary<string, string>
        {
            [LoopConfigHotReload.KeyMaxProgress] = "abc",
            [LoopConfigHotReload.KeyRequestTimeoutSeconds] = "-1",
            [LoopConfigHotReload.KeyRetryLimit] = "x",
            [LoopConfigHotReload.KeyLoopScope] = "not-a-scope",
        };

        LoopConfigHotReload.Apply(cfg, rows);

        Assert.AreEqual(AgentLoopConfig.DefaultMaxProgress, cfg.MaxProgress);
        Assert.AreEqual(AgentLoopConfig.DefaultRequestTimeout, cfg.RequestTimeout);
        Assert.AreEqual(AgentLoopConfig.DefaultRetryLimit, cfg.RetryLimit);
        Assert.AreEqual(Scope.LoopBody, cfg.LoopScope);
    }
}
