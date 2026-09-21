using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.HierarchyMethod.Nodes.Workflows;
using Usharp.Emit;
using Usharp.Semantics;

namespace Usharp.Emit.Tests;

/// <summary>
/// 批 U3：[Flow] → RoundSpec + 链转移表；判据 = 与既有 Config\workflows\agent-loop.json 的骨架等价
/// （步骤序 / FuncName / 入参键 / 结果键 / 成功准则逐条一致），原 C1（on_success 之外的静默截断）由闸A 编译期拦下。
/// </summary>
[TestClass]
public sealed class UsharpEmitTests
{
    // 工作流配置以 PascalCase 键声明（NodeId/FuncName/ParameterKeys/ResultKeys…）：大小写不敏感；
    // agent-loop.json 零映射声明（无 KeyMappingConfig），故无需配置专用转换器。
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static UsharpCompilation SampleCompilation()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "agent-loop.ushar");
        Assert.IsTrue(File.Exists(path), $"样例缺失（测试输出未见 samples）：{path}");
        return UsharpCompilation.Compile(new[] { (path, File.ReadAllText(path)) });
    }

    /// <summary>既有加载路径产出的骨架（真源 = 配置文件，非本框架自证）。</summary>
    private static RoundSpec ConfigRoundSpec()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "workflows", "agent-loop.json");
        Assert.IsTrue(File.Exists(path), $"工作流配置缺失：{path}");

        var spec = WorkflowSpecLoader.BuildRoundSpec(
            JsonSerializer.Deserialize<WorkflowDefinition>(File.ReadAllText(path), JsonOptions));
        Assert.IsNotNull(spec, "agent-loop.json 应可解析为骨架（PascalCase 键反序列化契约）");
        return spec!;
    }

    [TestMethod]
    public void Emit_AgentLoop_ProducesDeclaredRoundSpec()
    {
        var compilation = SampleCompilation();
        Assert.IsFalse(compilation.HasErrors, compilation.Report());

        var result = UsharpRoundSpecEmitter.Emit(compilation, "AgentLoop");
        Assert.IsFalse(result.HasErrors, result.Report());

        var spec = result.Spec!;
        Assert.AreEqual("AgentLoop", spec.Name, "骨架名取流号（配置文件名不参与比对）");
        Assert.AreEqual(RoundSpecSource.Declared, spec.Source);
        Assert.AreEqual(RoundSpecSuccessCriterion.DecisionWord, spec.SuccessCriterion, "success=decision");

        var expected = new[] { "guard_input", "provide_prompt", "cline_execute", "rule_match" };
        CollectionAssert.AreEqual(expected, spec.Steps.Select(s => s.FuncName).ToArray());
        CollectionAssert.AreEqual(expected, spec.Steps.Select(s => s.EffectiveNodeId).ToArray(),
            "稳定节点实例 ID = 步骤键");
    }

    [TestMethod]
    public void Emit_AgentLoop_IsEquivalentToConfigWorkflow()
    {
        var emitted = UsharpRoundSpecEmitter.Emit(SampleCompilation(), "AgentLoop").Spec!;
        var config = ConfigRoundSpec();

        Assert.AreEqual(config.Steps.Count, emitted.Steps.Count, "步骤数须与 agent-loop.json 主路径一致");
        for (var i = 0; i < config.Steps.Count; i++)
        {
            Assert.AreEqual(config.Steps[i].FuncName, emitted.Steps[i].FuncName, $"步骤 {i} FuncName");
            Assert.AreEqual(config.Steps[i].EffectiveNodeId, emitted.Steps[i].EffectiveNodeId, $"步骤 {i} 节点实例 ID");
            CollectionAssert.AreEqual(config.Steps[i].ParameterKeys.ToArray(), emitted.Steps[i].ParameterKeys.ToArray(),
                $"步骤 {i} 入参键（[Node] inports ↔ ParameterKeys）");
            CollectionAssert.AreEqual(config.Steps[i].ResultKeys.ToArray(), emitted.Steps[i].ResultKeys.ToArray(),
                $"步骤 {i} 结果键（[Node] outports ↔ ResultKeys）");
        }
        Assert.AreEqual(config.SuccessCriterion, emitted.SuccessCriterion, "成功准则");
    }

    [TestMethod]
    public void Emit_AgentLoop_ProducesChainTransitionTable()
    {
        var result = UsharpRoundSpecEmitter.Emit(SampleCompilation(), "AgentLoop");
        var table = result.TransitionTable!;

        Assert.AreEqual(3, table.Count, "4 步线性链 → 3 条转移；尾步不在表内 = 链终点");
        Assert.AreEqual("provide_prompt", table["guard_input"]);
        Assert.AreEqual("cline_execute", table["provide_prompt"]);
        Assert.AreEqual("rule_match", table["cline_execute"]);
        Assert.IsFalse(table.ContainsKey("rule_match"), "链终点不产生转移");
    }

    [TestMethod]
    public void Emit_SubFlow_IsEmittableAsOwnRoundSpec()
    {
        var sub = UsharpRoundSpecEmitter.Emit(SampleCompilation(), "SessionCline");

        Assert.IsFalse(sub.HasErrors, sub.Report());
        Assert.AreEqual(1, sub.Spec!.Steps.Count);
        Assert.AreEqual("cline_execute", sub.Spec.Steps[0].FuncName);
        Assert.AreEqual(0, sub.TransitionTable!.Count, "单步链无转移（该步即链终点）");
    }

    [TestMethod]
    public void Emit_UnknownFlow_ReportsUS5001()
    {
        var result = UsharpRoundSpecEmitter.Emit(SampleCompilation(), "NoSuchFlow");

        Assert.IsNull(result.Spec);
        Assert.IsTrue(result.Diagnostics.Any(d => d.Code == UsharpCodes.FlowNotDeclared), result.Report());
    }

    [TestMethod]
    public void Emit_StepWithoutNodeDeclaration_ReportsUS5002()
    {
        var compilation = UsharpCompilation.Compile(new[] { ("ghost.ushar", """
            [Space] Step: kind=keys
              start
              ghost

            [Flow] F: over=Step
              step start
              step ghost
            """) });

        var result = UsharpRoundSpecEmitter.Emit(compilation, "F");

        Assert.IsTrue(result.HasErrors);
        Assert.AreEqual(2, result.Diagnostics.Count(d => d.Code == UsharpCodes.FlowStepUnmapped),
            "两个步骤键均无 [Node] 声明：\n" + result.Report());
    }

    [TestMethod]
    public void Emit_UnknownSuccessCriterion_ReportsUS5003_AndFallsBackToDecision()
    {
        var compilation = UsharpCompilation.Compile(new[] { ("criterion.ushar", """
            [Space] Step: kind=keys
              start

            [Node] start: fn=start

            [Flow] F: over=Step | success=whatever
              step start
            """) });

        var result = UsharpRoundSpecEmitter.Emit(compilation, "F");

        Assert.IsTrue(result.Diagnostics.Any(d => d.Code == UsharpCodes.SuccessCriterionUnknown), result.Report());
        Assert.AreEqual(RoundSpecSuccessCriterion.DecisionWord, result.Spec!.SuccessCriterion);
    }

    [TestMethod]
    public void Emit_RunToEndCriterion_IsHonoured()
    {
        var compilation = UsharpCompilation.Compile(new[] { ("session.ushar", """
            [Space] Step: kind=keys
              answer

            [Node] answer: fn=answer | inports=question | outports=analyze_result

            [Flow] Session: over=Step | success=run_to_end
              step answer
            """) });

        var result = UsharpRoundSpecEmitter.Emit(compilation, "Session");

        Assert.IsFalse(result.HasErrors, result.Report());
        Assert.AreEqual(RoundSpecSuccessCriterion.RunToEnd, result.Spec!.SuccessCriterion);
        CollectionAssert.AreEqual(new[] { "question" }, result.Spec.Steps[0].ParameterKeys.ToArray());
    }
}
