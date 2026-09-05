using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.Skill;

namespace Agent.Core.Tests
{
    /// <summary>
    /// <see cref="InputSafetyGuard"/> 纯函数测试（自 Agent.Guardrails.GuardFunction.CheckInputSafety 抽取）。
    /// 危险模式子串扫描：SQL/Shell 特征命中即不安全；普通/空白输入安全。
    /// </summary>
    [TestClass]
    public class InputSafetyGuardTests
    {
        [TestMethod]
        public void Check_DangerousPatterns_ReturnsUnsafe()
        {
            Assert.IsFalse(InputSafetyGuard.Check("DROP TABLE users").IsSafe);
            Assert.IsFalse(InputSafetyGuard.Check("DELETE FROM t").IsSafe);
            Assert.IsFalse(InputSafetyGuard.Check("rm -rf /").IsSafe);
            Assert.IsFalse(InputSafetyGuard.Check("eval(x)").IsSafe);
            Assert.IsFalse(InputSafetyGuard.Check("exec(cmd)").IsSafe);
            Assert.IsFalse(InputSafetyGuard.Check("System(ps)").IsSafe);
            Assert.IsFalse(InputSafetyGuard.Check("Shell(ps)").IsSafe);
        }

        [TestMethod]
        public void Check_SafeOrEmpty_ReturnsSafe()
        {
            Assert.IsTrue(InputSafetyGuard.Check("请帮我分析数据").IsSafe);
            Assert.IsTrue(InputSafetyGuard.Check("").IsSafe);
            Assert.IsTrue(InputSafetyGuard.Check(null).IsSafe);
        }
    }
}
