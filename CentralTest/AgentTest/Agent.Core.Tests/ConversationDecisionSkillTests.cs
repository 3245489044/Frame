using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Agent.Contract.Llm;
using Agent.Core.Signal;
using Agent.Skill;
using Operation.LoopFoldMethod.Decision;
using Operation.LoopFoldMethod.Fold;
using Operation.LoopFoldMethod.Operator;
using Operation.LoopFoldMethod.Signal;

namespace Agent.Core.Tests;

/// <summary>
/// ConversationDecisionSkill（阶段 2b）测试：把 SkConversationFoldOperator（LLM 执行算子）适配为 IDecisionSkill。
/// 验证「执行当前 D → 表驱动产出下一步折叠链标识符 D′ / 链终点收敛」。零真实 LLM（假客户端），纯 CI 可跑。
/// </summary>
[TestClass]
public class ConversationDecisionSkillTests
{
    [TestMethod]
    public async Task DecideAsync_HasNextStep_ContinuesToNextKey()
    {
        // start → 有后继 collect → 决策产出 ContinueTo("collect")，状态推进。
        var llm = new FakeLlmClient();
        var op = new SkConversationFoldOperator(llm);
        var skill = new ConversationDecisionSkill(op,
            new Dictionary<string, string> { ["start"] = "collect" });
        var signal = new FoldSignal<ConversationFoldVertex>(FoldSignalKind.ExternalInput, "start", "请分析");

        var step = await skill.DecideAsync(
            new ConversationFoldState("s1", "分析", null), signal, Ctx(), default);

        Assert.IsInstanceOfType(step.Outcome, typeof(DecisionOutcome.ContinueTo));
        var outcome = (DecisionOutcome.ContinueTo)step.Outcome;
        Assert.AreEqual("collect", outcome.NextKey);
        Assert.AreEqual("回复", step.Vertex.OutputText);
    }

    [TestMethod]
    public async Task DecideAsync_NoNextStep_ConvergesToSuccess()
    {
        // 本步键不在转移表 → 链终点 → 决策产出 Terminate(Success)。
        var llm = new FakeLlmClient();
        var op = new SkConversationFoldOperator(llm);
        var skill = new ConversationDecisionSkill(op,
            new Dictionary<string, string> { ["start"] = "collect" });
        var signal = new FoldSignal<ConversationFoldVertex>(FoldSignalKind.StepCompleted, "act", "产出");

        var step = await skill.DecideAsync(
            new ConversationFoldState("s1", "分析", null), signal, Ctx(), default);

        Assert.IsInstanceOfType(step.Outcome, typeof(DecisionOutcome.Terminate));
        var outcome = (DecisionOutcome.Terminate)step.Outcome;
        Assert.AreEqual(TerminationSignal.Success, outcome.Signal);
    }

    private static FoldContext<ConversationFoldVertex> Ctx()
        => new(0, FoldBudget.Unlimited, Array.Empty<FoldVertex<ConversationFoldVertex>>());

    private sealed class FakeLlmClient : ILlmClient
    {
        public string Name => "fake";

        public Task<LlmResult> ChatAsync(
            string system, string user, IReadOnlyList<LlmMessage>? history = null, CancellationToken ct = default)
            => Task.FromResult(new LlmResult("回复"));

        public Task<LlmResult> ChatWithToolsAsync(
            string system, string user, IReadOnlyList<LlmTool>? tools = null,
            IReadOnlyList<LlmMessage>? history = null, CancellationToken ct = default)
            => Task.FromResult(new LlmResult("回复"));
    }
}
