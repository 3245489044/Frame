using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.Operation.Prompts;
using Plate.DslMethod.Parsing;
using TestInfrastructure;

namespace Agent.Operation.Tests;

/// <summary>
/// 统一 DSL 解析器与双模型投影方案——投影契约断言（P1/P2 验收）。
/// 旧 PromptDslParser / FormatSpecParser 已清理；以硬编码字面契约锁定 DslParser + 各投影器的产出，
/// 同时锁定 [Gate] 条件不污染 Fields 的回归点、L0 结构化产出与公理通道兼容。
/// </summary>
[TestClass]
[TestCategory("DslProjection")]
public class ProjectionDualRunTests
{
    private static string RepoRoot() => RepoRootLocator.Root;

    private static string SkillPath(string dsl) => dsl switch
    {
        "protocol" => Path.Combine(RepoRoot(), "Projects", "Agent", "skills", "internal", "l1l2", "SKILL.md"),
        "format" => Path.Combine(RepoRoot(), "Projects", "Agent", "skills", "l0", "l0-format", "SKILL.md"),
        "l0-system" => Path.Combine(RepoRoot(), "Projects", "Agent", "skills", "l0", "l0-system", "SKILL.md"),
        "l0-loop" => Path.Combine(RepoRoot(), "Projects", "Agent", "skills", "l0", "l0-loop", "SKILL.md"),
        "l0-logic" => Path.Combine(RepoRoot(), "Projects", "Agent", "skills", "l0", "l0-logic", "SKILL.md"),
        _ => throw new ArgumentOutOfRangeException(nameof(dsl)),
    };

    private static string SkillBody(string dsl) => StripFrontMatter(File.ReadAllText(SkillPath(dsl)));

    private static string StripFrontMatter(string text)
    {
        var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            int i = 1;
            while (i < lines.Length && lines[i].Trim() != "---") i++;
            return string.Join("\n", lines.Skip(i + 1));
        }
        return text;
    }

    // ---------- P1：协议契约（旧 PromptDslParser 已清理，以字面契约锁定产出） ----------

    [TestMethod]
    public void L1L2Projector_输出符合协议契约()
    {
        var body = SkillBody("protocol");

        var doc = DslParser.Parse(body, SkillPath("protocol"), "skill");
        var p = L1L2Projector.Project(doc);

        Assert.AreEqual("1.2.0", p.Version, "Version");

        Assert.IsTrue(p.L1.Template.Contains("## 任务目标", StringComparison.Ordinal), "L1.Template 应含任务目标节");
        Assert.IsTrue(p.L1.Template.Contains("{goal}") && p.L1.Template.Contains("{user_input}"), "L1.Template 槽位");
        Assert.IsTrue(p.L1.Generator.SystemTemplate.StartsWith("你是提示词分层框架的 L1 任务提示词生成器", StringComparison.Ordinal), "L1.System");
        Assert.IsTrue(p.L1.Generator.UserTemplate.Contains("{l0}") && p.L1.Generator.UserTemplate.Contains("{user_input}"), "L1.User 槽位");
        Assert.IsTrue(p.L1.Generation.Enabled, "L1.Enabled");
        Assert.AreEqual(0.0, p.L1.Generation.Temperature, "L1.Temperature");

        Assert.IsTrue(p.L2.ContinueTemplate.Contains("## 上一步执行结果", StringComparison.Ordinal), "L2.ContinueTemplate");
        Assert.IsTrue(p.L2.Generator.SystemTemplate.StartsWith("你是提示词分层框架的 L2 步骤提示词生成器", StringComparison.Ordinal), "L2.System");
        Assert.IsTrue(p.L2.Generator.UserTemplate.Contains("{exec_result}") && p.L2.Generator.UserTemplate.Contains("{hint}"), "L2.User 槽位");

        Assert.AreEqual(1, p.L2.ReplanConditions.Count, "ReplanConditions.Count");
        Assert.AreEqual("exec_result.text", p.L2.ReplanConditions[0].Field);
        Assert.AreEqual("contains_any", p.L2.ReplanConditions[0].Op);
        CollectionAssert.AreEquivalent(
            new[] { "需要语义重规划", "方案需重设计", "方向错误", "此路不通" },
            p.L2.ReplanConditions[0].Value.EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    // ---------- P1：协议 [Gate] 条件回归 ----------

    [TestMethod]
    public void L2ReplanConditions_Gate条件不污染Fields()
    {
        var body = SkillBody("protocol");
        var doc = DslParser.Parse(body, SkillPath("protocol"), "skill");

        var gate = doc.Declarations.First(d => d.Kind == Plate.DslMethod.DslSymbolKind.Gate && d.Name == "L2ReplanConditions");
        Assert.AreEqual(1, gate.Conditions.Count, "Conditions.Count");
        Assert.AreEqual("exec_result.text", gate.Conditions[0].Field);
        Assert.AreEqual("contains_any", gate.Conditions[0].Op);
        CollectionAssert.AreEquivalent(
            new[] { "需要语义重规划", "方案需重设计", "方向错误", "此路不通" },
            gate.Conditions[0].Value.ToArray());

        // 条件行不应以 k=v 字段形式污染 Fields
        Assert.IsFalse(gate.HasField("field"), "field 不应进 Fields");
        Assert.IsFalse(gate.HasField("op"), "op 不应进 Fields");
        Assert.IsFalse(gate.HasField("value"), "value 不应进 Fields");
    }

    // ---------- P1：格式契约（旧 FormatSpecParser 已清理，以字面契约锁定产出） ----------

    [TestMethod]
    public void FormatProjector_输出符合格式契约()
    {
        var body = SkillBody("format");

        var doc = DslParser.Parse(body, SkillPath("format"), "skill");
        var spec = FormatProjector.Project(doc, body);

        Assert.AreEqual(body.Trim(), spec.InstructionsText, "InstructionsText");
        Assert.IsTrue(spec.Schemas.ContainsKey("ReturnEnvelopeSchema"), "缺 ReturnEnvelopeSchema");
        Assert.IsTrue(spec.Schemas.ContainsKey("L1TaskPromptSchema"), "缺 L1TaskPromptSchema");
        Assert.IsTrue(spec.Schemas.ContainsKey("L2StepPromptSchema"), "缺 L2StepPromptSchema");

        // L1TaskPromptSchema：goal 必填 string，constraints/plan_summary 可选
        var l1 = spec.Schemas["L1TaskPromptSchema"];
        CollectionAssert.AreEquivalent(new[] { "goal" }, l1.Required.ToArray(), "L1.Required");
        Assert.IsTrue(l1.Fields.ContainsKey("goal") && l1.Fields["goal"].Type == "string", "L1.goal.type");
        Assert.IsTrue(l1.Fields.ContainsKey("constraints") && l1.Fields["constraints"].Optional, "L1.constraints.optional");
        Assert.IsTrue(l1.Fields.ContainsKey("plan_summary") && l1.Fields["plan_summary"].Optional, "L1.plan_summary.optional");

        // L2StepPromptSchema：instruction 必填，boundary_action 枚举
        var l2 = spec.Schemas["L2StepPromptSchema"];
        CollectionAssert.AreEquivalent(new[] { "instruction" }, l2.Required.ToArray(), "L2.Required");
        Assert.IsTrue(l2.Fields.ContainsKey("boundary_action"), "L2.boundary_action");
        CollectionAssert.AreEquivalent(
            new[] { "compact", "resubmit_with_info", "rescope", "escalate" },
            l2.Fields["boundary_action"].Enum.ToArray(), "L2.boundary_action.Enum");

        // ReturnEnvelopeSchema：signal/summary 必填，signal 枚举，next_hint 可选
        var env = spec.Schemas["ReturnEnvelopeSchema"];
        CollectionAssert.AreEquivalent(new[] { "signal", "summary" }, env.Required.ToArray(), "Env.Required");
        CollectionAssert.AreEquivalent(new[] { "DONE", "RETRY", "ERROR" }, env.Fields["signal"].Enum.ToArray(), "Env.signal.Enum");
        Assert.IsTrue(env.Fields.ContainsKey("next_hint") && env.Fields["next_hint"].Optional, "Env.next_hint.optional");
    }

    // ---------- P2：L0 结构化 ----------

    [TestMethod]
    public void L0Projector_L0各段产出结构化协议()
    {
        var merged = new L0Protocol();
        var sb = new System.Text.StringBuilder();
        foreach (var dsl in new[] { "format", "l0-system", "l0-loop", "l0-logic" })
        {
            var body = SkillBody(dsl);
            var doc = DslParser.Parse(body, SkillPath(dsl), "skill");
            merged.MergeFrom(L0Projector.Project(doc));
            if (!string.IsNullOrWhiteSpace(body))
            {
                sb.AppendLine(body);
                sb.AppendLine();
            }
        }

        Assert.IsTrue(merged.Axioms.Count > 0, "应有 Axioms");
        Assert.IsTrue(merged.Gates.Count > 0, "应有 Gates");
        Assert.IsTrue(merged.StateMachines.Count > 0, "应有 StateMachines");
        Assert.IsTrue(merged.Types.Count > 0, "应有 Types");
        Assert.IsTrue(merged.Schemas.Count > 0, "应有 Schemas");

        // 已知关键符号抽查
        Assert.IsTrue(merged.Axioms.Any(a => a.Name == "TerminationPriority"), "缺 TerminationPriority");
        Assert.IsTrue(merged.Gates.Any(g => g.Name == "ToolCallGuard"), "缺 ToolCallGuard");
        Assert.IsTrue(merged.StateMachines.Any(s => s.Name == "FailureBudgetEscalation"), "缺 FailureBudgetEscalation");
        Assert.IsTrue(merged.Types.Any(t => t.Name.StartsWith("Executor")), "缺 Type Executor");
        Assert.IsTrue(merged.Schemas.ContainsKey("L1TaskPromptSchema"), "缺 Schema L1TaskPromptSchema");
    }

    // ---------- 回归：clinerules 状态机（无 fence）不受 fenced 通道影响 ----------

    [TestMethod]
    public void DslParser_状态机无fence_保持续行步骤不进Template()
    {
        var body = SkillBody("l0-loop");
        var doc = DslParser.Parse(body, SkillPath("l0-loop"), "skill");

        var sm = doc.Declarations.First(d => d.Kind == Plate.DslMethod.DslSymbolKind.Sm && d.Name == "FailureBudgetEscalation");
        Assert.IsNull(sm.Template, "状态机不应有 Template");
        Assert.IsTrue(sm.Continuation.Count > 0, "状态机应保留步骤续行");
    }

    // ---------- 回归：clinerules 公理通道兼容（AxiomCodeGenerator 消费字段不变） ----------

    [TestMethod]
    public void DslParser_解析clinerules公式库_公理节点字段语义不变()
    {
        var path = Path.Combine(RepoRoot(), ".clinerules", "formula.md");
        var text = File.ReadAllText(path);
        var doc = DslParser.Parse(text, path, "formula");

        Assert.IsTrue(doc.Declarations.Count > 0, "应解析出声明");

        // [Gate] 工具策略：验证 Kind/Name/Fields/References 仍按旧语义填充（AxiomCodeGenerator 依赖点）
        var gate = doc.Declarations.FirstOrDefault(d => d.Kind == Plate.DslMethod.DslSymbolKind.Gate && d.Name == "FormatReassert");
        Assert.IsNotNull(gate, "缺 [Gate] FormatReassert");
        Assert.IsTrue(gate.References.Count > 0, "Gate 的 [CR] 引用集合应非空（AxiomCodeGenerator 消费 References）");
        Assert.IsTrue(gate.LineNumber > 0 && !string.IsNullOrWhiteSpace(gate.RawLine), "RawLine/LineNumber 存在");

        // [SM] 状态机无 fence → Template 为 null、Continuation 保留步骤（不误入 fenced 通道）
        var sm = doc.Declarations.FirstOrDefault(d => d.Kind == Plate.DslMethod.DslSymbolKind.Sm && d.Name == "DecideDrivenUpdate");
        Assert.IsNotNull(sm, "缺 [SM] DecideDrivenUpdate");
        Assert.IsNull(sm.Template, "[SM] 状态机不应被 fenced 通道捕获");
    }
}
