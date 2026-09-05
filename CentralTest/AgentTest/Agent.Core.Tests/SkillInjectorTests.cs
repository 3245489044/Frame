using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using Agent.Skill;

namespace Agent.Core.Tests
{
    /// <summary>
    /// <see cref="SkillInjector"/> 统一装配入口测试：输入守卫短路 / 热集关键词选配 / Rare 仅显式点名 / 无命中空块。
    /// 全部零 LLM、纯 BCL，可在纯 CI 跑。
    /// </summary>
    [TestClass]
    public class SkillInjectorTests
    {
        private static SkillDefinition Skill(string name, SkillModule module = SkillModule.Internal)
            => new()
            {
                Name = name,
                Module = module,
                Body = name + " 正文",
                Tags = new[] { name.ToLowerInvariant() },
            };

        [TestMethod]
        public void Inject_DangerousInput_Blocked_NoAssembly()
        {
            var hot = new[] { Skill("collect") };
            var result = SkillInjector.Inject("DROP TABLE users", "目标", "task", hot, Array.Empty<SkillDefinition>());

            Assert.IsNotNull(result.BlockedReason);
            StringAssert.Contains(result.BlockedReason!, "危险模式");
            Assert.AreEqual(0, result.SelectedSkills.Count);
            Assert.AreEqual(string.Empty, result.SkillBlock);
        }

        [TestMethod]
        public void Inject_SafeInput_SelectsHotByKeyword_AssemblesBlock()
        {
            var hot = new[] { Skill("collect"), Skill("analyze") };
            var result = SkillInjector.Inject("请收集并分析", "collect 数据", "task", hot, Array.Empty<SkillDefinition>());

            Assert.IsNull(result.BlockedReason);
            Assert.IsTrue(result.SelectedSkills.Any(s => s.Name == "collect"));
            StringAssert.Contains(result.SkillBlock, "## Skill: collect");
        }

        [TestMethod]
        public void Inject_RareOnlyWhenRequested_NotAutoMatched()
        {
            var hot = new[] { Skill("collect") };
            var rare = new[] { Skill("axiom-generator", SkillModule.Rare) };

            // 未点名 → Rare 不参与关键词自动匹配，不注入
            var noRare = SkillInjector.Inject("请生成 axiom", "axiom-generator", "task", hot, rare);
            Assert.IsFalse(noRare.SelectedSkills.Any(s => s.Name == "axiom-generator"));

            // 显式点名 → Rare 注入
            var withRare = SkillInjector.Inject("请生成 axiom", "axiom-generator", "task", hot, rare, new[] { "axiom-generator" });
            Assert.IsTrue(withRare.SelectedSkills.Any(s => s.Name == "axiom-generator"));
        }

        [TestMethod]
        public void Inject_NoMatch_EmptySkillBlock()
        {
            var hot = new[] { Skill("collect") };
            var result = SkillInjector.Inject("无关文本", "无关目标", "task", hot, Array.Empty<SkillDefinition>());

            Assert.AreEqual(0, result.SelectedSkills.Count);
            Assert.AreEqual(string.Empty, result.SkillBlock);
        }

        [TestMethod]
        public void Inject_L0SkillIsSystemBase_ExcludedFromOperationBlock()
        {
            var l0 = new SkillDefinition { Name = "prompt-l0", Module = SkillModule.L0, AlwaysOn = true, Body = "L0 必写协议正文" };
            var hot = new[] { l0, Skill("collect") };

            var result = SkillInjector.Inject("请收集", "collect 数据", "task", hot, Array.Empty<SkillDefinition>());

            Assert.IsNull(result.BlockedReason);
            Assert.IsFalse(result.SelectedSkills.Any(s => s.Name == "prompt-l0"), "L0 不混入操作块");
            Assert.IsFalse(result.SkillBlock.Contains("prompt-l0"));
            Assert.IsTrue(result.SelectedSkills.Any(s => s.Name == "collect"), "普通操作技能仍按关键词选中");
        }

        [TestMethod]
        public void Inject_L0EmptyBody_FallsBackNull()
        {
            var l0 = new SkillDefinition { Name = "prompt-l0", Module = SkillModule.L0 }; // 正文留空
            var hot = new[] { l0, Skill("collect") };

            var result = SkillInjector.Inject("请收集", "collect", "task", hot, Array.Empty<SkillDefinition>());

            Assert.IsFalse(result.SelectedSkills.Any(s => s.Name == "prompt-l0"), "L0 不混入操作块");
        }
    }
}
