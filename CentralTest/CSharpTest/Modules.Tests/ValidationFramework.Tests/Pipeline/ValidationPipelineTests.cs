
using System.Text.Json;
using ValidationFramework.Abstractions;
using ValidationFramework.Engine;
using ValidationFramework.Pipeline;

namespace ValidationFramework.Tests.Pipeline;

[TestClass]
public class ValidationPipelineTests
{
    private static readonly JsonElement EmptyConfig = JsonDocument.Parse("{}").RootElement;

    [TestMethod]
    public void 构造_应设置名称()
    {
        var pipeline = new ValidationPipeline("Pipe", new List<KeyValuePair<string, ValidationEngine>>());

        Assert.AreEqual("Pipe", pipeline.Name);
    }

    [TestMethod]
    public void 构造_名称null_应抛异常()
    {
        Assert.ThrowsException<ArgumentNullException>(
            () => new ValidationPipeline(null!, new List<KeyValuePair<string, ValidationEngine>>()));
    }

    [TestMethod]
    public void Builder_AddStage_应注册阶段()
    {
        var builder = new ValidationPipelineBuilder("Pipe");
        builder.AddStage("A", new ValidationEngine("EngineA"));

        var pipeline = builder.Build();

        Assert.AreEqual("Pipe", pipeline.Name);
    }

    [TestMethod]
    public void Builder_同名阶段_应抛异常()
    {
        var builder = new ValidationPipelineBuilder("Pipe");
        builder.AddStage("A", new ValidationEngine("EngineA"));

        Assert.ThrowsException<InvalidOperationException>(
            () => builder.AddStage("A", new ValidationEngine("EngineB")));
    }

    [TestMethod]
    public async Task ExecuteAsync_应执行所有阶段()
    {
        var engineA = new ValidationEngine("EngineA");
        engineA.RegisterRule(new EmitRule("R-A", Severity.Warning));
        var engineB = new ValidationEngine("EngineB");
        engineB.RegisterRule(new EmitRule("R-B", Severity.Warning));

        var pipeline = new ValidationPipeline(
            "Pipe",
            new[]
            {
                new KeyValuePair<string, ValidationEngine>("StageA", engineA),
                new KeyValuePair<string, ValidationEngine>("StageB", engineB)
            });

        var report = await pipeline.ExecuteAsync(EmptyConfig, new ValidationContext());

        Assert.AreEqual(2, report.Diagnostics.Count);
        Assert.AreEqual(2, report.WarningCount);
        Assert.IsTrue(report.IsValid);
    }

    [TestMethod]
    public async Task ExecuteAsync_严格模式_阶段级阻断()
    {
        var engineA = new ValidationEngine("EngineA");
        engineA.RegisterRule(new EmitRule("R-A", Severity.Error));
        var engineB = new ValidationEngine("EngineB");
        engineB.RegisterRule(new EmitRule("R-B", Severity.Warning));

        var pipeline = new ValidationPipeline(
            "Pipe",
            new[]
            {
                new KeyValuePair<string, ValidationEngine>("StageA", engineA),
                new KeyValuePair<string, ValidationEngine>("StageB", engineB)
            },
            ValidationMode.Strict);

        var report = await pipeline.ExecuteAsync(EmptyConfig, new ValidationContext());

        Assert.AreEqual(1, report.ErrorCount);
        Assert.AreEqual(0, report.WarningCount);
        Assert.AreEqual(1, report.Diagnostics.Count);
    }

    [TestMethod]
    public async Task ExecuteAsync_宽松模式_错误不阻断后续阶段()
    {
        var engineA = new ValidationEngine("EngineA");
        engineA.RegisterRule(new EmitRule("R-A", Severity.Error));
        var engineB = new ValidationEngine("EngineB");
        engineB.RegisterRule(new EmitRule("R-B", Severity.Warning));

        var pipeline = new ValidationPipeline(
            "Pipe",
            new[]
            {
                new KeyValuePair<string, ValidationEngine>("StageA", engineA),
                new KeyValuePair<string, ValidationEngine>("StageB", engineB)
            },
            ValidationMode.Loose);

        var report = await pipeline.ExecuteAsync(EmptyConfig, new ValidationContext());

        Assert.AreEqual(1, report.ErrorCount);
        Assert.AreEqual(1, report.WarningCount);
        Assert.AreEqual(2, report.Diagnostics.Count);
    }

    [TestMethod]
    public async Task ExecuteAsync_管线Strict_上下文Loose_应提升上下文()
    {
        var engineA = new ValidationEngine("EngineA");
        engineA.RegisterRule(new EmitRule("R-A", Severity.Error));
        var engineB = new ValidationEngine("EngineB");
        engineB.RegisterRule(new EmitRule("R-B", Severity.Warning));

        var pipeline = new ValidationPipeline(
            "Pipe",
            new[]
            {
                new KeyValuePair<string, ValidationEngine>("StageA", engineA),
                new KeyValuePair<string, ValidationEngine>("StageB", engineB)
            },
            ValidationMode.Strict);

        var report = await pipeline.ExecuteAsync(EmptyConfig, new ValidationContext(ValidationMode.Loose));

        Assert.AreEqual(1, report.Diagnostics.Count);
    }

    [TestMethod]
    public async Task ExecuteAsync_静默上下文_不提升严格模式()
    {
        var engineA = new ValidationEngine("EngineA");
        engineA.RegisterRule(new EmitRule("R-A", Severity.Error));

        var pipeline = new ValidationPipeline(
            "Pipe",
            new[] { new KeyValuePair<string, ValidationEngine>("StageA", engineA) },
            ValidationMode.Strict);

        var report = await pipeline.ExecuteAsync(EmptyConfig, new ValidationContext(ValidationMode.Silent));

        Assert.AreEqual(0, report.Diagnostics.Count);
    }

    [TestMethod]
    public async Task ExecuteAsync_阶段状态_应标记完成()
    {
        var engine = new ValidationEngine("EngineA");
        var pipeline = new ValidationPipeline(
            "Pipe",
            new[] { new KeyValuePair<string, ValidationEngine>("StageA", engine) });

        await pipeline.ExecuteAsync(EmptyConfig, new ValidationContext());

        Assert.IsTrue(true);
    }

    [TestMethod]
    public async Task ExecuteJsonAsync_应解析JSON执行()
    {
        var engine = new ValidationEngine("EngineA");
        engine.RegisterRule(new AlwaysPassRule());
        var pipeline = new ValidationPipeline(
            "Pipe",
            new[] { new KeyValuePair<string, ValidationEngine>("StageA", engine) });

        var report = await pipeline.ExecuteJsonAsync("{}");

        Assert.AreEqual("Pipe", report.ValidatorName);
        Assert.IsTrue(report.IsValid);
    }

    [TestMethod]
    public void Builder_AddStage_null引擎_应抛异常()
    {
        var builder = new ValidationPipelineBuilder("Pipe");

        Assert.ThrowsException<ArgumentNullException>(
            () => builder.AddStage("A", null!));
    }


    private sealed class EmitRule : IValidationRule
    {
        public string RuleName { get; }
        public string Description => RuleName;
        public ValidationRuleCategory Category => ValidationRuleCategory.Structure;
        private readonly Severity _severity;

        public EmitRule(string name, Severity severity)
        {
            RuleName = name;
            _severity = severity;
        }

        public Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
            JsonElement config, IValidationContext context, CancellationToken cancellationToken = default)
        {
            var d = _severity == Severity.Error
                ? ValidationDiagnostic.Error(RuleName, "err")
                : ValidationDiagnostic.Warning(RuleName, "warn");
            return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(new[] { d });
        }
    }

    private sealed class AlwaysPassRule : IValidationRule
    {
        public string RuleName => "Pass";
        public string Description => "Pass";
        public ValidationRuleCategory Category => ValidationRuleCategory.Structure;

        public Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
            JsonElement config, IValidationContext context, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(Array.Empty<ValidationDiagnostic>());
    }
}