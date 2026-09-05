using System;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.Contract.Answer;
using Agent.Core.TaskPool;

namespace Agent.Core.Tests
{
    /// <summary>
    /// 达标判定策略（<see cref="AnswerAcceptancePolicy"/>）验证：
    /// ① 纯判定（FirstNonEmpty / Criteria / None）确定性、非 LLM 自主；
    /// ② 可序列化，随 <see cref="AgentTaskItem.AnswerSpec"/>（PayloadJson）跨任务池边界 round-trip 全量保留。
    /// 背景：此前达标源为不可序列化 Func（acceptWhen），任务池路径未注入 → answerAccepted 恒 false → 任何回答都不可能 Success（必然 TripTerminated）。
    /// 本策略把达标判定上升为可序列化数据并在评估层单源计算，两条执行路径行为一致。
    /// </summary>
    [TestClass]
    public class AnswerAcceptancePolicyTests
    {
        [TestMethod]
        public void FirstNonEmpty_AcceptsAnyNonBlank()
        {
            var p = new AnswerAcceptancePolicy { Mode = AnswerAcceptMode.FirstNonEmpty };
            Assert.IsFalse(p.Accepts(""));
            Assert.IsFalse(p.Accepts("   "));
            Assert.IsTrue(p.Accepts("回答内容"));
            Assert.IsTrue(p.Accepts("x"));
        }

        [TestMethod]
        public void Criteria_RequiresMinLengthAndAllKeywords()
        {
            var p = new AnswerAcceptancePolicy
            {
                Mode = AnswerAcceptMode.Criteria,
                MinLength = 8,
                RequiredKeywords = new[] { "Cline", "代理" },
            };
            Assert.IsFalse(p.Accepts("太短"));
            Assert.IsFalse(p.Accepts("Cline 一句话"));                  // 长度 < 8
            Assert.IsTrue(p.Accepts("Cline 编码代理的核心能力包括多轮执行")); // 长度足 + 含全部关键词
            Assert.IsFalse(p.Accepts("Cline 编码工具很强大且足够"));      // 缺关键词 "代理"
        }

        [TestMethod]
        public void None_NeverAccepts()
        {
            var p = new AnswerAcceptancePolicy { Mode = AnswerAcceptMode.None };
            Assert.IsFalse(p.Accepts("任何回答"));
            Assert.IsFalse(p.Accepts(""));
        }

        [TestMethod]
        public void Defaults_ToFirstNonEmpty()
        {
            var p = new AnswerAcceptancePolicy();
            Assert.AreEqual(AnswerAcceptMode.FirstNonEmpty, p.Mode);
            Assert.IsTrue(p.Accepts("非空回答"));
        }

        [TestMethod]
        public void RoundTrips_ThroughAgentTaskItemAnswerSpec()
        {
            var item = new AgentTaskItem
            {
                TaskKey = "t1",
                UserInput = "q",
                ExecutorId = "stateless-answer",
                AnswerSpec = new AnswerTaskSpec
                {
                    MaxRounds = 2,
                    AcceptancePolicy = new AnswerAcceptancePolicy
                    {
                        Mode = AnswerAcceptMode.Criteria,
                        MinLength = 10,
                        RequiredKeywords = new[] { "Cline" },
                    },
                },
            };

            var json = JsonSerializer.Serialize(item);
            var restored = JsonSerializer.Deserialize<AgentTaskItem>(json)!;

            Assert.IsNotNull(restored.AnswerSpec);
            var policy = restored.AnswerSpec!.AcceptancePolicy;
            Assert.IsNotNull(policy);
            Assert.AreEqual(AnswerAcceptMode.Criteria, policy!.Mode);
            Assert.AreEqual(10, policy.MinLength);
            CollectionAssert.AreEqual(
                new[] { "Cline" },
                policy.RequiredKeywords.Select(k => k).ToArray());
        }
    }
}
