
using System.Text.Json;
using ValidationFramework.Abstractions;
using ValidationFramework.Engine;

namespace ValidationFramework.Tests.Engine;

[TestClass]
public class ValidationEngineTests
{
    private static readonly JsonElement EmptyConfig = JsonDocument.Parse("{}").RootElement;

    [TestMethod]
    public void 默认引擎_应设置名称与零规则()
    {
        var engine = new ValidationEngine();

        Assert.AreEqual("DefaultValidationEngine", engine.Name);
        Assert.AreEqual(0, engine.RuleCount);
    }

    [TestMethod]
    public void 自定义名称_应生效()
    {
        var engine = new ValidationEngine("MyEngine");

        Assert.AreEqual("MyEngine", engine.Name);
    }

    [TestMethod]
    public void 名称null_应抛异常()
    {
        Assert.ThrowsException<ArgumentNullException>(() => new ValidationEngine(null!));
    }

    [TestMethod]
    public void RegisterRule_应增加规则数()
    {
        var engine = new ValidationEngine();
        engine.RegisterRule(new StubRule("R1", ValidationRuleCategory.Structure));

        Assert.AreEqual(1, engine.RuleCount);
    }

    [TestMethod]
    public void RegisterRule_同名重复_应抛异常()
    {
        var engine = new ValidationEngine();
        engine.RegisterRule(new StubRule("R1", ValidationRuleCategory.Structure));

        Assert.ThrowsException<InvalidOperationException>(
            () => engine.RegisterRule(new StubRule("R1", ValidationRuleCategory.Reference)));
    }

    [TestMethod]
    public void RegisterRule_null_应抛异常()
    {
        var engine = new ValidationEngine();

        Assert.ThrowsException<ArgumentNullException>(() => engine.RegisterRule(null!));
    }

    [TestMethod]
    public void RegisterRules_批量注册_应全部生效()
    {
        var engine = new ValidationEngine();
        engine.RegisterRules(new IValidationRule[]
        {
            new StubRule("R1", ValidationRuleCategory.Structure),
            new StubRule("R2", ValidationRuleCategory.Reference)
        });

        Assert.AreEqual(2, engine.RuleCount);
    }

    [TestMethod]
    public void UnregisterRule_应移除规则()
    {
        var engine = new ValidationEngine();
        engine.RegisterRule(new StubRule("R1", ValidationRuleCategory.Structure));

        var result = engine.UnregisterRule("R1");

        Assert.AreSame(engine, result);
        Assert.AreEqual(0, engine.RuleCount);
    }

    [TestMethod]
    public void UnregisterRule_不存在_应无副作用()
    {
        var engine = new ValidationEngine();

        engine.UnregisterRule("Nope");

        Assert.AreEqual(0, engine.RuleCount);
    }

    [TestMethod]
    public void GetRules_应返回规则副本()
    {
        var engine = new ValidationEngine();
        engine.RegisterRule(new StubRule("R1", ValidationRuleCategory.Structure));

        var rules = engine.GetRules();

        Assert.AreEqual(1, rules.Count);
        Assert.AreEqual("R1", rules[0].RuleName);
    }

    [TestMethod]
    public void GetRulesByCategory_应仅返回对应分类()
    {
        var engine = new ValidationEngine();
        engine.RegisterRule(new StubRule("Struct", ValidationRuleCategory.Structure));
        engine.RegisterRule(new StubRule("Ref", ValidationRuleCategory.Reference));

        var refRules = engine.GetRulesByCategory(ValidationRuleCategory.Reference);

        Assert.AreEqual(1, refRules.Count);
        Assert.AreEqual("Ref", refRules[0].RuleName);
    }

    [TestMethod]
    public void RegisterUnrestrictedRule_应纳入GetRulesByCategory()
    {
        var engine = new ValidationEngine();
        engine.RegisterUnrestrictedRule(new StubRule("U1", ValidationRuleCategory.Contract));

        Assert.AreEqual(1, engine.GetRulesByCategory(ValidationRuleCategory.Contract).Count);
    }

    [TestMethod]
    public async Task ValidateAsync_应聚合所有规则诊断()
    {
        var engine = new ValidationEngine();
        engine.RegisterRule(new EmitRule("E1", ValidationRuleCategory.Structure, Severity.Error));
        engine.RegisterRule(new EmitRule("E2", ValidationRuleCategory.Reference, Severity.Warning));

        var report = await engine.ValidateAsync(EmptyConfig, new ValidationContext());

        Assert.AreEqual(2, report.Diagnostics.Count);
        Assert.AreEqual(1, report.ErrorCount);
        Assert.AreEqual(1, report.WarningCount);
    }

    [TestMethod]
    public async Task ValidateAsync_静默模式_应返回空报告()
    {
        var engine = new ValidationEngine();
        engine.RegisterRule(new EmitRule("E1", ValidationRuleCategory.Structure, Severity.Error));

        var report = await engine.ValidateAsync(EmptyConfig, new ValidationContext(ValidationMode.Silent));

        Assert.AreEqual(0, report.Diagnostics.Count);
        Assert.IsTrue(report.IsValid);
    }

    [TestMethod]
    public async Task ValidateAsync_严格模式_错误阻断后续分类()
    {
        var engine = new ValidationEngine();
        var executed = new List<string>();
        engine.RegisterRule(new TrackingRule("Struct-Error", ValidationRuleCategory.Structure, executed, Severity.Error));
        engine.RegisterRule(new TrackingRule("Ref-Rule", ValidationRuleCategory.Reference, executed, Severity.Info));

        var report = await engine.ValidateAsync(EmptyConfig, new ValidationContext(ValidationMode.Strict));

        Assert.AreEqual(1, report.ErrorCount);
        CollectionAssert.AreEqual(new[] { "Struct-Error" }, executed);
    }

    [TestMethod]
    public async Task ValidateAsync_宽松模式_错误不阻断后续分类()
    {
        var engine = new ValidationEngine();
        var executed = new List<string>();
        engine.RegisterRule(new TrackingRule("Struct-Error", ValidationRuleCategory.Structure, executed, Severity.Error));
        engine.RegisterRule(new TrackingRule("Ref-Rule", ValidationRuleCategory.Reference, executed, Severity.Info));

        var report = await engine.ValidateAsync(EmptyConfig, new ValidationContext(ValidationMode.Loose));

        Assert.AreEqual(1, report.ErrorCount);
        CollectionAssert.AreEqual(new[] { "Struct-Error", "Ref-Rule" }, executed);
    }

    [TestMethod]
    public async Task ValidateAsync_规则异常_应隔离为错误诊断()
    {
        var engine = new ValidationEngine();
        engine.RegisterRule(new ThrowingRule("Thrower", ValidationRuleCategory.Structure));
        engine.RegisterRule(new EmitRule("Normal", ValidationRuleCategory.Reference, Severity.Warning));

        var report = await engine.ValidateAsync(EmptyConfig, new ValidationContext());

        Assert.AreEqual(1, report.ErrorCount);
        Assert.AreEqual(1, report.WarningCount);
        Assert.IsTrue(report.Diagnostics.Any(d => d.RuleName == "Thrower" && d.Severity == Severity.Error));
    }

    [TestMethod]
    public async Task ValidateAsync_RestrictToCategories_应过滤分类()
    {
        var engine = new ValidationEngine();
        engine.RegisterRule(new EmitRule("Struct", ValidationRuleCategory.Structure, Severity.Warning));
        engine.RegisterRule(new EmitRule("Ref", ValidationRuleCategory.Reference, Severity.Warning));
        engine.RestrictToCategories(new[] { ValidationRuleCategory.Reference });

        var report = await engine.ValidateAsync(EmptyConfig, new ValidationContext());

        Assert.AreEqual(0, report.Diagnostics.Count(d => d.RuleName == "Struct"));
        Assert.AreEqual(1, report.Diagnostics.Count(d => d.RuleName == "Ref"));
    }

    [TestMethod]
    public async Task ValidateAsync_unrestricted规则_不受分类过滤影响()
    {
        var engine = new ValidationEngine();
        engine.RegisterRule(new EmitRule("Normal", ValidationRuleCategory.Structure, Severity.Warning));
        engine.RegisterUnrestrictedRule(new EmitRule("Unrestricted", ValidationRuleCategory.Reference, Severity.Warning));
        engine.RestrictToCategories(new[] { ValidationRuleCategory.Reference });

        var report = await engine.ValidateAsync(EmptyConfig, new ValidationContext());

        Assert.AreEqual(0, report.Diagnostics.Count(d => d.RuleName == "Normal"));
        Assert.AreEqual(1, report.Diagnostics.Count(d => d.RuleName == "Unrestricted"));
    }

    [TestMethod]
    public async Task ValidateAsync_取消令牌_应抛异常()
    {
        var engine = new ValidationEngine();
        engine.RegisterRule(new StubRule("R1", ValidationRuleCategory.Structure));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(
            () => engine.ValidateAsync(EmptyConfig, new ValidationContext(), cts.Token));
    }


    private abstract class TestRuleBase : IValidationRule
    {
        public string RuleName { get; }
        public string Description { get; }
        public ValidationRuleCategory Category { get; }

        protected TestRuleBase(string name, ValidationRuleCategory category)
        {
            RuleName = name;
            Description = name;
            Category = category;
        }

        public abstract Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
            JsonElement config,
            IValidationContext context,
            CancellationToken cancellationToken = default);
    }

    private sealed class StubRule : TestRuleBase
    {
        public StubRule(string name, ValidationRuleCategory category) : base(name, category) { }

        public override Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
            JsonElement config, IValidationContext context, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(Array.Empty<ValidationDiagnostic>());
    }

    private sealed class EmitRule : TestRuleBase
    {
        private readonly Severity _severity;

        public EmitRule(string name, ValidationRuleCategory category, Severity severity) : base(name, category)
            => _severity = severity;

        public override Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
            JsonElement config, IValidationContext context, CancellationToken cancellationToken = default)
        {
            var d = _severity switch
            {
                Severity.Error => ValidationDiagnostic.Error(RuleName, "err"),
                Severity.Warning => ValidationDiagnostic.Warning(RuleName, "warn"),
                _ => ValidationDiagnostic.Info(RuleName, "info")
            };
            return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(new[] { d });
        }
    }

    private sealed class TrackingRule : TestRuleBase
    {
        private readonly List<string> _executed;
        private readonly Severity _severity;

        public TrackingRule(string name, ValidationRuleCategory category, List<string> executed, Severity severity)
            : base(name, category)
        {
            _executed = executed;
            _severity = severity;
        }

        public override Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
            JsonElement config, IValidationContext context, CancellationToken cancellationToken = default)
        {
            _executed.Add(RuleName);
            var d = _severity == Severity.Error
                ? ValidationDiagnostic.Error(RuleName, "err")
                : ValidationDiagnostic.Info(RuleName, "info");
            return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(new[] { d });
        }
    }

    private sealed class ThrowingRule : TestRuleBase
    {
        public ThrowingRule(string name, ValidationRuleCategory category) : base(name, category) { }

        public override Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
            JsonElement config, IValidationContext context, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("boom");
    }
}