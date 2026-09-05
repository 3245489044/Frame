using Agent.Axioms.Scheduling;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Agent.Axioms.Tests
{
    /// <summary>
    /// StrategyGuard 统一守卫测试：策略防幻觉判定（目标 ∈ 可用集）与置信度归一。
    /// 覆盖 TransitionGuard / LlmStateDirector / ConfigStateDirector 三路径共用语义。
    /// </summary>
    [TestClass]
    public sealed class StrategyGuardTests
    {
        private static readonly string[] Available = { "standard", "aggressive", "careful" };

        [TestMethod]
        public void VerifyStrategy_NullOrWhitespace_Reject()
        {
            Assert.AreEqual(StrategyGuard.StrategyMatch.Reject,
                StrategyGuard.VerifyStrategy(null, Available, "standard"));
            Assert.AreEqual(StrategyGuard.StrategyMatch.Reject,
                StrategyGuard.VerifyStrategy("  ", Available, "standard"));
        }

        [TestMethod]
        public void VerifyStrategy_NotInAvailableSet_Reject()
        {
            Assert.AreEqual(StrategyGuard.StrategyMatch.Reject,
                StrategyGuard.VerifyStrategy("unknown", Available, "standard"));
        }

        [TestMethod]
        public void VerifyStrategy_ValidDifferentFromCurrent_Pass()
        {
            Assert.AreEqual(StrategyGuard.StrategyMatch.Pass,
                StrategyGuard.VerifyStrategy("aggressive", Available, "standard"));
        }

        [TestMethod]
        public void VerifyStrategy_SameAsCurrent_Held()
        {
            Assert.AreEqual(StrategyGuard.StrategyMatch.Held,
                StrategyGuard.VerifyStrategy("standard", Available, "standard"));
        }

        [TestMethod]
        public void VerifyStrategy_DefaultOrdinalCaseSensitive()
        {
            // 默认 Ordinal：大小写不一致 → 视为不在可用集（策略 ID 为精确标识符）。
            Assert.AreEqual(StrategyGuard.StrategyMatch.Reject,
                StrategyGuard.VerifyStrategy("Aggressive", Available, "standard"));
        }

        [TestMethod]
        public void VerifyStrategy_AllowIgnoreCase_MatchesCaseInsensitive()
        {
            // LLM 自由文本输入可显式放宽为 OrdinalIgnoreCase。
            Assert.AreEqual(StrategyGuard.StrategyMatch.Pass,
                StrategyGuard.VerifyStrategy("Aggressive", Available, "standard", allowIgnoreCase: true));
        }

        [TestMethod]
        public void NormalizeConfidence_InRange_ReturnsSame()
        {
            Assert.AreEqual(0.0, StrategyGuard.NormalizeConfidence(0.0));
            Assert.AreEqual(0.5, StrategyGuard.NormalizeConfidence(0.5));
            Assert.AreEqual(1.0, StrategyGuard.NormalizeConfidence(1.0));
        }

        [TestMethod]
        public void NormalizeConfidence_OutOfRange_ReturnsDefaultOne()
        {
            Assert.AreEqual(1.0, StrategyGuard.NormalizeConfidence(-0.1));
            Assert.AreEqual(1.0, StrategyGuard.NormalizeConfidence(1.5));
        }
    }
}
