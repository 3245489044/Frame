using Microsoft.VisualStudio.TestTools.UnitTesting;
using Plate.DslMethod.Abstractions;
using Plate.DslMethod.CodeGen;
using Plate.DslMethod.Logic;
using TestInfrastructure;

namespace Agent.Core.Tests;

/// <summary>
/// 公理代码库治理测试（阶段5）：扫描 Agent.Core [BindAxiom] 绑定、校验悬空/漂移/缺口、反向追溯、反转重生成幂等。
/// </summary>
[TestClass]
[TestCategory("DslMethod")]
public class DslAxiomBindingTests
{
    private static readonly HashSet<string> Subset = new(StringComparer.OrdinalIgnoreCase)
    {
        "RoundLifecycle", "TerminationPriority", "EvaluationOutcomeSet", "DecisionOutputSet", "ClassifyError",
        "BoundaryPressureModel", "BoundaryTriggerArbiter", "SpatialBoundary",
        "OutputWellFormed", "FormatFidelity", "FirstCallTransient",
        "InformationMonotonicity", "SingleImplementation", "StratifiedReflectionSchema",
    };

    private static readonly Dictionary<string, AxiomLevel> Overrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EvaluationOutcomeSet"] = AxiomLevel.L1,
        ["DecisionOutputSet"] = AxiomLevel.L1,
    };

    private static string RepoRoot() => RepoRootLocator.Root;
    private static string PromptsDir() => Path.Combine(RepoRoot(), "Projects", "Agent", "Config", "prompts");
    private static string AxiomsDir() => Path.Combine(RepoRoot(), "Projects", "Agent", "Axioms");

    private static AxiomScanner ScanAgentCore()
        => new AxiomScanner().Scan(typeof(Agent.Core.Nodes.WorkflowRunner).Assembly);

    [TestMethod]
    public void Scan_AgentCore_Bindings全部解析到公理索引()
    {
        var scanner = ScanAgentCore();

        Assert.IsTrue(scanner.Bindings.Count >= 14, $"Agent.Core 应有 ≥14 条代码绑定，实际 {scanner.Bindings.Count}。");

        // 悬空校验：每条绑定的 AxiomType 必须落在公理索引中
        foreach (var b in scanner.Bindings)
        {
            Assert.IsNotNull(b.AxiomType, "绑定缺少公理类型");
            Assert.IsNotNull(scanner.AxiomOf(b.AxiomType), $"绑定 target 悬空: {b.AxiomName} on {b.DeclaringType?.Name}");
        }

        // 代码公理索引齐全
        foreach (var name in Subset)
            Assert.IsNotNull(scanner.AxiomOf(name), $"公理索引缺 {name}");
    }

    [TestMethod]
    public void Drift_包含RoundStepInvoker与AgentRunCancellation()
    {
        var drift = ScanAgentCore().FindDrift();

        Assert.IsTrue(drift.Any(d => d.Contains("RoundStepInvoker")), "漂移清单应含 RoundStepInvoker");
        Assert.IsTrue(drift.Any(d => d.Contains("AgentRunCancellation")), "漂移清单应含 AgentRunCancellation");
    }

    [TestMethod]
    public void Gap_SpatialBoundaryEvaluator标记为Gap()
    {
        var gap = ScanAgentCore().Bindings
            .FirstOrDefault(b => b.DeclaringType?.Name == nameof(Agent.Axioms.Boundaries.SpatialBoundaryEvaluator));

        Assert.IsNotNull(gap, "应存在 SpatialBoundaryEvaluator 绑定");
        Assert.AreEqual(BindStatus.Gap, gap.Status, "SpatialBoundary 为缺公理(gap)状态");
    }

    [TestMethod]
    public void Reverse_BoundaryPressureModel两个实现者()
    {
        var scanner = ScanAgentCore();
        Assert.IsTrue(scanner.Reverse.TryGetValue("BoundaryPressureModel", out var impls), "应存在 BoundaryPressureModel 反向引用");
        Assert.AreEqual(2, impls.Count, "BoundaryPressureModel 应有 ContextEstimator + BoundaryReconstructor 两个实现者");
    }

    [TestMethod]
    public void Regenerate_与提交产物逐字节一致()
    {
        var gen = new AxiomCodeGenerator();
        var tmp = Path.Combine(Path.GetTempPath(), "axioms_regen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = gen.Generate(PromptsDir(), tmp, new AxiomCodeGeneratorOptions
            {
                IncludeNames = Subset,
                LevelOverrides = Overrides,
                EmitMappingReport = false,
            });

            Assert.AreEqual(Subset.Count, result.Count, "再生成公理数应与白名单子集一致（每符号唯一）");

            foreach (var r in result)
            {
                var rel = Path.GetRelativePath(tmp, r.OutputPath);
                var committed = Path.Combine(AxiomsDir(), rel);
                Assert.IsTrue(File.Exists(committed), $"缺少提交产物: {rel}");
                Assert.AreEqual(File.ReadAllText(committed), File.ReadAllText(r.OutputPath), $"再生成与提交产物不一致: {rel}");
            }
        }
        finally
        {
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        }
    }
}
