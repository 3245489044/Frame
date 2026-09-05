
using System.Text.Json;
using ValidationFramework.Abstractions;
using ValidationFramework.Engine;

namespace ValidationFramework.Tests.Engine;

[TestClass]
public class ValidationEngineOptionsTests
{
    [TestMethod]
    public void 默认选项_应启用前三分类关闭后两者()
    {
        var options = new ValidationEngineOptions();

        Assert.IsTrue(options.EnableStructure);
        Assert.IsTrue(options.EnableReference);
        Assert.IsTrue(options.EnableSemantic);
        Assert.IsFalse(options.EnableRuntime);
        Assert.IsFalse(options.EnableContract);
        Assert.AreEqual("ConfiguredValidationEngine", options.Name);
    }

    [TestMethod]
    public void ShallowCopy_应保留属性()
    {
        var options = new ValidationEngineOptions
        {
            Name = "CopyTest",
            EnableContract = true
        };

        var copy = options.ShallowCopy();

        Assert.AreEqual("CopyTest", copy.Name);
        Assert.IsTrue(copy.EnableContract);
        Assert.AreEqual("CopyTest", options.Name);
    }

    [TestMethod]
    public void ShallowCopy_规则列表应独立副本()
    {
        var rule = new DummyRule("R1");
        var options = new ValidationEngineOptions
        {
            AdditionalRules = new[] { rule }
        };

        var copy = options.ShallowCopy();
        copy.AdditionalRules = new[] { new DummyRule("R2") };

        Assert.AreEqual(1, options.AdditionalRules.Count);
        Assert.AreEqual("R1", options.AdditionalRules[0].RuleName);
        Assert.AreEqual("R2", copy.AdditionalRules[0].RuleName);
    }

    [TestMethod]
    public void Build_无工厂_仅注册附加规则()
    {
        var rule = new DummyRule("Only");
        var options = new ValidationEngineOptions
        {
            AdditionalRules = new[] { rule }
        };

        var engine = options.Build();

        Assert.AreEqual("Only", engine.GetRules().Single().RuleName);
    }

    [TestMethod]
    public void Build_工厂规则_按开启分类注册()
    {
        var options = new ValidationEngineOptions
        {
            RuleFactory = category => new[]
            {
                new DummyRule($"Rule-{category}")
            }
        };

        var engine = options.Build();

        Assert.AreEqual(3, engine.RuleCount);
        Assert.IsTrue(engine.GetRules().Any(r => r.RuleName == $"Rule-{ValidationRuleCategory.Structure}"));
        Assert.IsFalse(engine.GetRules().Any(r => r.RuleName == $"Rule-{ValidationRuleCategory.Runtime}"));
    }

    [TestMethod]
    public void Build_关闭分类开关_对应规则不注册()
    {
        var options = new ValidationEngineOptions
        {
            EnableSemantic = false,
            RuleFactory = category => new[] { new DummyRule($"Rule-{category}") }
        };

        var engine = options.Build();

        Assert.AreEqual(2, engine.RuleCount);
    }

    [TestMethod]
    public void Build_附加规则_不受分类开关限制()
    {
        var options = new ValidationEngineOptions
        {
            EnableStructure = false,
            EnableReference = false,
            EnableSemantic = false,
            AdditionalRules = new[] { new DummyRule("Always") }
        };

        var engine = options.Build();

        Assert.AreEqual(1, engine.RuleCount);
        Assert.AreEqual("Always", engine.GetRules().Single().RuleName);
    }

    [TestMethod]
    public void Build_同名冲突_应抛异常()
    {
        var options = new ValidationEngineOptions
        {
            RuleFactory = _ => new[] { new DummyRule("Dup") },
            AdditionalRules = new[] { new DummyRule("Dup") }
        };

        Assert.ThrowsException<InvalidOperationException>(() => options.Build());
    }

    [TestMethod]
    public async Task Build_运行期分类过滤_应阻止禁用分类执行()
    {
        var executed = new List<string>();
        var options = new ValidationEngineOptions
        {
            RuleFactory = category => new[]
            {
                new TrackingDummyRule($"Rule-{category}", category, executed)
            }
        };

        var engine = options.Build();
        using var doc = JsonDocument.Parse("{}");
        await engine.ValidateAsync(doc.RootElement, new ValidationContext());

        Assert.IsTrue(executed.Any(n => n == $"Rule-{ValidationRuleCategory.Structure}"));
        Assert.IsTrue(executed.Any(n => n == $"Rule-{ValidationRuleCategory.Reference}"));
        Assert.IsTrue(executed.Any(n => n == $"Rule-{ValidationRuleCategory.Semantic}"));
        Assert.IsFalse(executed.Any(n => n == $"Rule-{ValidationRuleCategory.Runtime}"));
        Assert.IsFalse(executed.Any(n => n == $"Rule-{ValidationRuleCategory.Contract}"));
    }

    private class DummyRule : IValidationRule
    {
        public string RuleName { get; }
        public string Description { get; }
        public ValidationRuleCategory Category { get; }

        public DummyRule(string name, ValidationRuleCategory category = ValidationRuleCategory.Structure)
        {
            RuleName = name;
            Description = name;
            Category = category;
        }

        public virtual Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
            JsonElement config,
            IValidationContext context,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(Array.Empty<ValidationDiagnostic>());
    }

    private sealed class TrackingDummyRule : DummyRule
    {
        private readonly List<string> _executed;

        public TrackingDummyRule(string name, ValidationRuleCategory category, List<string> executed)
            : base(name, category)
        {
            _executed = executed;
        }

        public override Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
            JsonElement config, IValidationContext context, CancellationToken cancellationToken = default)
        {
            _executed.Add(RuleName);
            return base.ValidateAsync(config, context, cancellationToken);
        }
    }
}