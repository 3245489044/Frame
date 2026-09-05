
using System.Text.Json;
using ParameterResolver.Validation.Rules;
using ParameterResolver.Validation.Rules.Structure;

namespace ParameterResolver.Validation.Tests;

[TestClass]
public class ValidationEngineTests
{
    [TestMethod]
    public void RegisterRule_ShouldAddRule()
    {
        var engine = new ValidationEngine("TestEngine");

        engine.RegisterRule(new EventValueTypeRule());

        Assert.AreEqual(1, engine.RuleCount);
    }

    [TestMethod]
    public void RegisterRule_DuplicateName_ShouldThrow()
    {
        var engine = new ValidationEngine("TestEngine");
        engine.RegisterRule(new EventValueTypeRule());

        Assert.ThrowsException<InvalidOperationException>(
            () => engine.RegisterRule(new EventValueTypeRule()));
    }

    [TestMethod]
    public void UnregisterRule_ShouldRemoveRule()
    {
        var engine = new ValidationEngine("TestEngine");
        engine.RegisterRule(new EventValueTypeRule());
        Assert.AreEqual(1, engine.RuleCount);

        engine.UnregisterRule("EventValueType");

        Assert.AreEqual(0, engine.RuleCount);
    }

    [TestMethod]
    public async Task ValidateAsync_ValidConfig_ShouldReturnValidReport()
    {
        var engine = new ValidationEngine("TestEngine");
        engine.RegisterRule(new EventValueTypeRule());
        engine.RegisterRule(new LayoutFormatRule());

        var json = """
        {
            "controls": [
                {
                    "name": "btn1",
                    "events": {
                        "Click": ["OnClick"]
                    },
                    "layout": {
                        "x": 0.1,
                        "y": 0.2
                    }
                }
            ]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;
        var context = new ConfigValidationContext(ValidationMode.Loose);

        var report = await engine.ValidateAsync(config, context);

        Assert.IsTrue(report.IsValid);
        Assert.AreEqual(0, report.ErrorCount);
    }

    [TestMethod]
    public async Task ValidateAsync_InvalidConfig_ShouldReturnDiagnostics()
    {
        var engine = new ValidationEngine("TestEngine");
        engine.RegisterRule(new EventValueTypeRule());

        var json = """
        {
            "controls": [
                {
                    "name": "btn1",
                    "events": {
                        "Click": 123
                    }
                }
            ]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;
        var context = new ConfigValidationContext(ValidationMode.Loose);

        var report = await engine.ValidateAsync(config, context);

        Assert.IsFalse(report.IsValid);
        Assert.AreEqual(1, report.ErrorCount);
    }

    [TestMethod]
    public async Task ValidateAsync_StrictMode_ShouldStopOnFirstError()
    {
        var engine = new ValidationEngine("TestEngine");
        engine.RegisterRule(new EventValueTypeRule());
        engine.RegisterRule(new LayoutFormatRule());

        var json = """
        {
            "controls": [
                {
                    "name": "btn1",
                    "events": {
                        "Click": 123
                    },
                    "layout": {
                        "x": 1.5
                    }
                }
            ]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;
        var context = new ConfigValidationContext(ValidationMode.Strict);

        var report = await engine.ValidateAsync(config, context);

        Assert.IsFalse(report.IsValid);
        Assert.IsTrue(report.ErrorCount >= 1);
    }

    [TestMethod]
    public async Task ValidateAsync_SilentMode_ShouldReturnEmptyReport()
    {
        var engine = new ValidationEngine("TestEngine");
        engine.RegisterRule(new EventValueTypeRule());

        var json = """
        {
            "controls": [
                {
                    "name": "btn1",
                    "events": {
                        "Click": "OnClick"
                    }
                }
            ]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;
        var context = new ConfigValidationContext(ValidationMode.Silent);

        var report = await engine.ValidateAsync(config, context);

        Assert.IsTrue(report.IsValid);
        Assert.AreEqual(0, report.Diagnostics.Count);
    }

    [TestMethod]
    public void GetRulesByCategory_ShouldFilterCorrectly()
    {
        var engine = new ValidationEngine("TestEngine");
        engine.RegisterRule(new EventValueTypeRule());
        engine.RegisterRule(new LayoutFormatRule());

        var structureRules = engine.GetRulesByCategory(ValidationRuleCategory.Structure);

        Assert.AreEqual(2, structureRules.Count);
        Assert.IsTrue(structureRules.All(r => r.Category == ValidationRuleCategory.Structure));
    }
}
