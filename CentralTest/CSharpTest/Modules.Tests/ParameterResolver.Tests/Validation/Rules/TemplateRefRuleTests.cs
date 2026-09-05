
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;
using ParameterResolver.Validation.Providers;
using ParameterResolver.Validation.Rules.Reference;

namespace ParameterResolver.Validation.Tests.Rules;

[TestClass]
public class TemplateRefRuleTests
{
    private readonly TemplateRefRule _rule = new();

    [TestMethod]
    public async Task ValidateAsync_NoTemplateRegistry_ShouldReturnInfo()
    {
        var context = new ConfigValidationContext();
        var json = """{"controls":[{"name":"btn1"}]}""";
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual(Severity.Info, diagnostics[0].Severity);
        Assert.IsTrue(diagnostics[0].Message.Contains("模板注册表提供者未配置"));
    }

    [TestMethod]
    public async Task ValidateAsync_ValidTemplateRef_ShouldReturnNoDiagnostics()
    {
        var context = new ConfigValidationContext
        {
            TemplateRegistry = new MockTemplateRegistryProvider(new[] { "defaultTemplate", "customTemplate" })
        };
        var json = """
        {
            "controls": [{
                "name": "panel1",
                "template": "defaultTemplate"
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, context);

        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_InvalidTemplateRef_ShouldReturnError()
    {
        var context = new ConfigValidationContext
        {
            TemplateRegistry = new MockTemplateRegistryProvider(new[] { "defaultTemplate" })
        };
        var json = """
        {
            "controls": [{
                "name": "panel1",
                "template": "nonExistentTemplate"
            }]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, context);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual("TemplateRef", diagnostics[0].RuleName);
        Assert.AreEqual(Severity.Error, diagnostics[0].Severity);
        Assert.IsTrue(diagnostics[0].Message.Contains("nonExistentTemplate"));
    }

    [TestMethod]
    public async Task ValidateAsync_MultipleTemplates_ShouldValidateAll()
    {
        var context = new ConfigValidationContext
        {
            TemplateRegistry = new MockTemplateRegistryProvider(new[] { "t1" })
        };
        var json = """
        {
            "controls": [
                { "name": "p1", "template": "t1" },
                { "name": "p2", "template": "t2" },
                { "name": "p3", "template": "t3" }
            ]
        }
        """;
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, context);

        Assert.AreEqual(2, diagnostics.Count);
        Assert.IsTrue(diagnostics.All(d => d.RuleName == "TemplateRef"));
    }

    [TestMethod]
    public async Task ValidateAsync_NoTemplateField_ShouldReturnNoDiagnostics()
    {
        var context = new ConfigValidationContext
        {
            TemplateRegistry = new MockTemplateRegistryProvider(new[] { "t1" })
        };
        var json = """{"controls":[{"name":"btn1","type":"button"}]}""";
        var config = JsonDocument.Parse(json).RootElement;

        var diagnostics = await _rule.ValidateAsync(config, context);

        Assert.AreEqual(0, diagnostics.Count);
    }

    private class MockTemplateRegistryProvider : ITemplateRegistryProvider
    {
        private readonly HashSet<string> _templates;

        public MockTemplateRegistryProvider(string[] templates)
        {
            _templates = new HashSet<string>(templates);
        }

        public string Name => "MockTemplateRegistry";

        public IReadOnlySet<string> GetTemplateNames() => _templates;

        public bool HasTemplate(string templateName) => _templates.Contains(templateName);

        public string? GetTemplateDescription(string templateName) =>
            _templates.Contains(templateName) ? $"Template: {templateName}" : null;
    }
}
