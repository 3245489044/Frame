
using System.Text.Json;
using BasicMethod.Declarative;
using ValidationFramework.Abstractions;
using ValidationFramework.Declarative;

namespace ValidationFramework.Tests.Declarative;

[TestClass]
public class ConstraintValidationRuleTests
{
    [TestMethod]
    public void 提取器返回null_应跳过校验()
    {
        var rule = new ConstraintValidationRule<string>(
            new StringConstraint(),
            (JsonElement _, IValidationContext _) => (string?)null);

        using var doc = JsonDocument.Parse("{}");
        var result = rule.ValidateAsync(doc.RootElement, new ValidationContext()).GetAwaiter().GetResult();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void 约束满足_应无诊断()
    {
        var rule = new ConstraintValidationRule<int>(
            CreateAlwaysPassConstraint(),
            (_, _) => 42);

        using var doc = JsonDocument.Parse("{}");
        var result = rule.ValidateAsync(doc.RootElement, new ValidationContext()).GetAwaiter().GetResult();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void 约束不满足_应生成错误诊断()
    {
        var rule = new ConstraintValidationRule<int>(
            CreateAlwaysFailConstraint("值必须为正"),
            (_, _) => -1);

        using var doc = JsonDocument.Parse("{}");
        var result = rule.ValidateAsync(doc.RootElement, new ValidationContext()).GetAwaiter().GetResult();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(Severity.Error, result[0].Severity);
        Assert.AreEqual("MustBePositive", result[0].RuleName);
        Assert.IsTrue(result[0].Message.Contains("值必须为正"));
    }

    [TestMethod]
    public void 约束不满足_应使用FilePath作为Source()
    {
        var rule = new ConstraintValidationRule<int>(
            CreateAlwaysFailConstraint(),
            (_, _) => 1);

        using var doc = JsonDocument.Parse("{}");
        var context = new ValidationContext(ValidationMode.Loose, "file.json");
        var result = rule.ValidateAsync(doc.RootElement, context).GetAwaiter().GetResult();

        Assert.AreEqual("file.json", result[0].SourceLocation?.Source);
    }

    [TestMethod]
    public void pathSelector_应设置JsonPath()
    {
        var rule = new ConstraintValidationRule<int>(
            CreateAlwaysFailConstraint(),
            (_, _) => 1,
            pathSelector: (_, _) => "$.target");

        using var doc = JsonDocument.Parse("{}");
        var result = rule.ValidateAsync(doc.RootElement, new ValidationContext()).GetAwaiter().GetResult();

        Assert.AreEqual("$.target", result[0].SourceLocation?.JsonPath);
    }

    [TestMethod]
    public void RuleName和Description_应取自约束()
    {
        var rule = new ConstraintValidationRule<int>(
            CreateAlwaysFailConstraint(),
            (_, _) => 1);

        Assert.AreEqual("MustBePositive", rule.RuleName);
        Assert.AreEqual("值必须为正数", rule.Description);
        Assert.AreEqual(ValidationRuleCategory.Contract, rule.Category);
    }

    [TestMethod]
    public void 指定分类_应生效()
    {
        var rule = new ConstraintValidationRule<int>(
            CreateAlwaysFailConstraint(),
            (_, _) => 1,
            ValidationRuleCategory.Structure);

        Assert.AreEqual(ValidationRuleCategory.Structure, rule.Category);
    }

    [TestMethod]
    public void ConstraintRuleFactory_Predicate_应创建规则()
    {
        var rule = ConstraintRuleFactory.Predicate<string>(
            "NotEmpty",
            "字符串不能为空",
            s => !string.IsNullOrEmpty(s),
            (_, _) => "abc");

        Assert.AreEqual("NotEmpty", rule.RuleName);
        using var doc = JsonDocument.Parse("{}");
        var result = rule.ValidateAsync(doc.RootElement, new ValidationContext()).GetAwaiter().GetResult();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ConstraintRuleFactory_Predicate_不满足_应生成诊断()
    {
        var rule = ConstraintRuleFactory.Predicate<string>(
            "NotEmpty",
            "字符串不能为空",
            s => !string.IsNullOrEmpty(s),
            (_, _) => "",
            s => $"值为空: {s}");

        using var doc = JsonDocument.Parse("{}");
        var result = rule.ValidateAsync(doc.RootElement, new ValidationContext()).GetAwaiter().GetResult();

        Assert.AreEqual(1, result.Count);
        Assert.IsTrue(result[0].Message.Contains("值为空"));
    }


    private static IConstraint<int> CreateAlwaysFailConstraint(string reason = "失败")
        => new FakeConstraint("MustBePositive", "值必须为正数", false, reason);

    private static IConstraint<int> CreateAlwaysPassConstraint()
        => new FakeConstraint("AlwaysPass", "总是通过", true, null);

    private sealed class FakeConstraint : IConstraint<int>
    {
        public string Name { get; }
        public string Description { get; }
        private readonly bool _satisfied;
        private readonly string? _reason;

        public FakeConstraint(string name, string description, bool satisfied, string? reason)
        {
            Name = name;
            Description = description;
            _satisfied = satisfied;
            _reason = reason;
        }

        public ConstraintResult Check(int input)
            => _satisfied
                ? ConstraintResult.Satisfied(Name, Description, input)
                : ConstraintResult.Failed(Name, Description, _reason ?? "失败", input);
    }

    private sealed class StringConstraint : IConstraint<string>
    {
        public string Name => "MustNotBeNull";
        public string Description => "字符串不能为 null";

        public ConstraintResult Check(string input)
            => ConstraintResult.Failed(Name, Description, "值为 null", input);
    }
}