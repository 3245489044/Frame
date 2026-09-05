using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Agent.Assembly.Assembly;
using Agent.Assembly.Binding;
using Agent.Assembly.Canonical;
using Agent.Assembly.Ports;
using Agent.Assembly.Projection;
using Agent.Operation.Prompts;
using Agent.Skill;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Conversation;
using Share.Conversation.ContextBinding;

namespace Agent.Core.Tests
{
    /// <summary>
    /// 统一装配框架（Agent.Assembly）单测：验证「UI 显示 ≡ LLM 输入」由构造保证（TH-12 / 单一真相）。
    /// P4 验收：Canonical → Llm → Ui 双向逐字一致 + 零额外（ContextBindingValidator R1–R6 全绿）。
    /// </summary>
    [TestClass]
    public class AgentAssemblyTests
    {
        private sealed class FakeSource : IAssemblySource
        {
            public string L0 => "L0 系统提示词（基底）";
            public PromptProtocol Protocol => new();
            public IReadOnlyList<SkillDefinition> HotSkills => Array.Empty<SkillDefinition>();
            public IReadOnlyList<SkillDefinition> RareSkills => Array.Empty<SkillDefinition>();
            public IReadOnlyList<string>? RequestedSkills(string scopeKey) => null;

            public Task<ResolvedL1> ResolveL1Async(
                string taskKey, string userInput, string goal, string constraints, string planSummary,
                string l0, bool isFirstStep)
                => Task.FromResult(new ResolvedL1("L1 任务级提示词（填槽）", false));

            public Task<ResolvedL2> ResolveL2Async(
                string taskKey, string userInput, string goal, string constraints, string planSummary,
                object? execResult, string? hint, string l0)
                => Task.FromResult(new ResolvedL2("L2 步骤级提示词（填槽）", false, null));
        }

        [TestMethod]
        public async Task PromptAssembler_ProducesSnapshotConsistentWithResult()
        {
            var ctx = new AssemblyContext { UserInput = "u", Goal = "g", TaskKey = "t", IsFirstStep = false };
            var result = await PromptAssembler.Default.AssembleAsync(ctx, new FakeSource()).ConfigureAwait(false);

            Assert.AreEqual("L0 系统提示词（基底）", result.Snapshot.L0);
            Assert.AreEqual("L1 任务级提示词（填槽）", result.Snapshot.L1);
            Assert.AreEqual("L2 步骤级提示词（填槽）", result.Snapshot.L2);
            // system 槽位 = L0 + SkillBlock（空技能块），与装配结果逐字一致。
            Assert.AreEqual(PromptText.Assemble(result.L0, result.SkillBlock), result.System);
            Assert.AreEqual(result.Snapshot.L0, result.L0);
        }

        [TestMethod]
        public void UiView_Equals_LlmView_SameSource()
        {
            // 同一 Canonical + 同一写侧快照 → UiView 与 LlmView 逐字一致（构造保证）。
            var snapshot = PromptSnapshotBuilder.Build(
                "L0 系统提示词", "", "L1 任务级提示词", "L2 步骤级提示词", new[] { "skill-a" });
            var canonical = new CanonicalContext("task", "t", new List<ConversationMessage>
            {
                new(ConversationRole.User, null, null, "历史1", 1, DateTimeOffset.UtcNow,
                    Binding: new MessageBinding(AnchorKey.History(1), MessageOrigin.LlmHistory, FeedTarget.History, PromptSnapshotBuilder.Hash("历史1"))),
                new(ConversationRole.Tool, "node-a", "node-a", "节点产出", 1, DateTimeOffset.UtcNow,
                    Binding: new MessageBinding(AnchorKey.Node("node-a"), MessageOrigin.LlmHistory, FeedTarget.History, PromptSnapshotBuilder.Hash("节点产出"))),
            });

            var ui = UiViewProjector.Build(canonical, snapshot);
            var llm = LlmViewProjector.Build(canonical, snapshot);

            Assert.AreEqual(ui.Count, llm.Count);
            for (var i = 0; i < ui.Count; i++)
            {
                Assert.AreEqual(ui[i].Binding!.Key.Value, llm[i].Binding!.Key.Value, $"anchor[{i}]");
                Assert.AreEqual(ui[i].Text, llm[i].Text, $"text[{i}]");
            }
        }

        [TestMethod]
        public void ContextBindingValidator_ReturnsNoViolations_ForSameSource()
        {
            var snapshot = PromptSnapshotBuilder.Build(
                "L0 系统提示词", "## 技能操作指南", "L1 任务级提示词", "L2 步骤级提示词", new[] { "skill-a" });
            var canonical = new CanonicalContext("task", "t", new List<ConversationMessage>
            {
                new(ConversationRole.User, null, null, "历史1", 1, DateTimeOffset.UtcNow,
                    Binding: new MessageBinding(AnchorKey.History(1), MessageOrigin.LlmHistory, FeedTarget.History, PromptSnapshotBuilder.Hash("历史1"))),
            });

            var ui = UiViewProjector.Build(canonical, snapshot);
            var llm = LlmViewProjector.Build(canonical, snapshot);

            var violations = ContextBindingValidator.Validate(ui, llm, snapshot);
            Assert.AreEqual(0, violations.Count, "UI ≡ LLM 应全绿：" + string.Join(" | ", violations));
        }

        [TestMethod]
        public void HistoryToWire_ProjectsHistoryOnly()
        {
            var snapshot = PromptSnapshotBuilder.Build("L0", "", "L1", "L2", null);
            var canonical = new CanonicalContext("task", "t", new List<ConversationMessage>
            {
                new(ConversationRole.User, null, null, "历史1", 1, DateTimeOffset.UtcNow,
                    Binding: new MessageBinding(AnchorKey.History(1), MessageOrigin.LlmHistory, FeedTarget.History, PromptSnapshotBuilder.Hash("历史1"))),
            });

            var wire = LlmViewProjector.HistoryToWire(canonical);
            Assert.AreEqual(1, wire.Count);
            Assert.AreEqual("历史1", wire[0].Content);
        }
    }
}
