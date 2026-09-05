
using ValidationFramework.Abstractions;
using ValidationFramework.Factory;
using ValidationFramework.Feedback;
using ValidationFramework.Pipeline;

namespace ValidationFramework.Tests.Factory;

[TestClass]
public class ValidationFrameworkFactoryTests
{
    [TestMethod]
    public void CreateEngine_默认选项_应含内置契约规则()
    {
        var engine = ValidationFrameworkFactory.CreateEngine();

        var contractRules = engine.GetRulesByCategory(ValidationRuleCategory.Contract);

        Assert.AreEqual("ConfiguredValidationEngineEngine", engine.Name);
        Assert.IsTrue(contractRules.Count >= 3);
        CollectionAssert.Contains(
            contractRules.Select(r => r.RuleName).ToList(),
            "FunctionContract");
        CollectionAssert.Contains(
            contractRules.Select(r => r.RuleName).ToList(),
            "WorkflowLegality");
        CollectionAssert.Contains(
            contractRules.Select(r => r.RuleName).ToList(),
            "ScopeGuard");
    }

    [TestMethod]
    public void CreateEngine_禁用内置契约规则_应无契约规则()
    {
        var engine = ValidationFrameworkFactory.CreateEngine(
            new ValidationFrameworkOptions
            {
                EnableBuiltInContractRules = false,
                EnableContract = true
            });

        Assert.AreEqual(0, engine.GetRulesByCategory(ValidationRuleCategory.Contract).Count);
    }

    [TestMethod]
    public void CreateEngine_自定义名称_应追加Engine后缀()
    {
        var engine = ValidationFrameworkFactory.CreateEngine(
            new ValidationFrameworkOptions { Name = "My" });

        Assert.AreEqual("MyEngine", engine.Name);
    }

    [TestMethod]
    public void CreateEngine_不修改传入options()
    {
        var options = new ValidationFrameworkOptions
        {
            Name = "NoMutate",
            AdditionalRules = new[] { new DummyRule("R1") }
        };

        ValidationFrameworkFactory.CreateEngine(options);

        Assert.AreEqual("NoMutate", options.Name);
        Assert.AreEqual(1, options.AdditionalRules.Count);
    }

    [TestMethod]
    public void CreateEngine_附加规则_应注册为无限制规则()
    {
        var rule = new DummyRule("Always");
        var engine = ValidationFrameworkFactory.CreateEngine(
            new ValidationFrameworkOptions
            {
                AdditionalRules = new[] { rule }
            });

        Assert.IsTrue(engine.GetRules().Any(r => r.RuleName == "Always"));
    }

    [TestMethod]
    public void CreatePipeline_应创建单阶段管线()
    {
        var pipeline = ValidationFrameworkFactory.CreatePipeline(
            new ValidationFrameworkOptions { Name = "Pipe" });

        Assert.AreEqual("PipePipeline", pipeline.Name);
    }

    [TestMethod]
    public void CreatePipeline_默认模式_应宽松()
    {
        var pipeline = ValidationFrameworkFactory.CreatePipeline();

        Assert.IsNotNull(pipeline);
    }

    [TestMethod]
    public void CreateFeedbackFormatter_应创建格式化器()
    {
        var formatter = ValidationFrameworkFactory.CreateFeedbackFormatter();

        Assert.IsNotNull(formatter);
    }

    [TestMethod]
    public void CreateFeedbackFormatter_includeInfo_应生效()
    {
        var report = ValidationReport.Create(
            "Engine",
            new[] { ValidationDiagnostic.Info("R1", "info") });

        var formatter = ValidationFrameworkFactory.CreateFeedbackFormatter(includeInfo: true);
        var json = formatter.ToLlmFeedback(report);

        Assert.IsTrue(json.Contains("\"message\": \"info\""));
    }

    [TestMethod]
    public void CreateFeedbackSink_应创建接收器()
    {
        var sink = ValidationFrameworkFactory.CreateFeedbackSink();

        Assert.IsNotNull(sink);
    }

    [TestMethod]
    public void CreateFeedbackSink_自定义logger_应调用()
    {
        var log = new List<string>();
        var sink = ValidationFrameworkFactory.CreateFeedbackSink(log.Add);
        var report = ValidationReport.Empty("Engine");

        sink.Emit(report, null);

        Assert.AreEqual(1, log.Count);
    }

    private sealed class DummyRule : IValidationRule
    {
        public string RuleName { get; }
        public string Description => RuleName;
        public ValidationRuleCategory Category => ValidationRuleCategory.Structure;

        public DummyRule(string name) => RuleName = name;

        public Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
            System.Text.Json.JsonElement config,
            IValidationContext context,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(Array.Empty<ValidationDiagnostic>());
    }
}