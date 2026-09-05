using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.TaskStatusPool.Loops;

namespace Agent.Core.Tests
{
    /// <summary>
    /// Agent 决策式循环终止收敛（<see cref="AgentLoopTermination"/>，原 LoopTermination 迁入）测试。
    /// 覆盖：终态判定（TS/TO/TM）、最终决策收敛优先级与兜底语义。
    /// </summary>
    [TestClass]
    public class AgentLoopTerminationTests
    {
        [TestMethod]
        public void ConvergeFinal_PrefersEngineTerminal()
        {
            Assert.AreEqual(LoopDecision.Timeout,
                AgentLoopTermination.ConvergeFinal(LoopDecision.Timeout, LoopDecision.Success));
        }

        [TestMethod]
        public void ConvergeFinal_FallsBackToLastDecision()
        {
            Assert.AreEqual(LoopDecision.Success,
                AgentLoopTermination.ConvergeFinal(LoopDecision.Continue, LoopDecision.Success));
        }

        [TestMethod]
        public void ConvergeFinal_DefaultsToContinue()
        {
            Assert.AreEqual(LoopDecision.Continue,
                AgentLoopTermination.ConvergeFinal(LoopDecision.Continue, LoopDecision.None));
        }

        [TestMethod]
        public void IsTerminal_TrueForBudgetaryTerminationOnly()
        {
            Assert.IsTrue(AgentLoopTermination.IsTerminal(LoopDecision.Success));
            Assert.IsTrue(AgentLoopTermination.IsTerminal(LoopDecision.Timeout));
            Assert.IsTrue(AgentLoopTermination.IsTerminal(LoopDecision.TripTerminated));
            Assert.IsFalse(AgentLoopTermination.IsTerminal(LoopDecision.Continue));
            Assert.IsFalse(AgentLoopTermination.IsTerminal(LoopDecision.None));
            Assert.IsFalse(AgentLoopTermination.IsTerminal(LoopDecision.SwitchStrategy));
        }
    }
}
