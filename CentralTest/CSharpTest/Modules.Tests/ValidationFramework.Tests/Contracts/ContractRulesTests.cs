
using System.Text.Json;
using ValidationFramework.Abstractions;
using ValidationFramework.Contracts;
using ValidationFramework.Providers;

namespace ValidationFramework.Tests.Contracts;

[TestClass]
public class ContractRulesTests
{
    [TestMethod]
    public void FunctionContract_无签名提供者_应跳过()
    {
        var rule = new FunctionContractRule();
        using var doc = JsonDocument.Parse("""{ "events": { "click": ["f1"] } }""");

        var result = rule.ValidateAsync(doc.RootElement, new ValidationContext()).GetAwaiter().GetResult();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void FunctionContract_未注册函数_应生成错误诊断()
    {
        var rule = new FunctionContractRule();
        const string json = """
        {
          "events": {
            "click": ["Registered", "Missing"]
          }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var provider = CreateSignatureProvider(new[] { "Registered" });
        var context = new ValidationContext(ValidationMode.Loose, "cfg.json", json);
        context.WithProvider<IContractSignatureProvider>(provider);

        var result = rule.ValidateAsync(doc.RootElement, context).GetAwaiter().GetResult();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("FunctionContract", result[0].RuleName);
        Assert.AreEqual(Severity.Error, result[0].Severity);
        Assert.IsTrue(result[0].Message.Contains("Missing"));
        Assert.IsNotNull(result[0].RepairSuggestion);
        Assert.IsTrue(result[0].RepairSuggestion!.Contains("Registered"));
        Assert.AreEqual("$.events.click[1]", result[0].SourceLocation?.JsonPath);
    }

    [TestMethod]
    public void FunctionContract_全部注册_应无诊断()
    {
        var rule = new FunctionContractRule();
        using var doc = JsonDocument.Parse("""{ "events": { "click": ["f1"] } }""");
        var context = new ValidationContext();
        context.WithProvider<IContractSignatureProvider>(CreateSignatureProvider(new[] { "f1" }));

        var result = rule.ValidateAsync(doc.RootElement, context).GetAwaiter().GetResult();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void WorkflowLegality_无对象注册表_应跳过()
    {
        var rule = new WorkflowLegalityRule();
        using var doc = JsonDocument.Parse("""{ "Workflows": { "W": { "steps": [] } } }""");

        var result = rule.ValidateAsync(doc.RootElement, new ValidationContext()).GetAwaiter().GetResult();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void WorkflowLegality_缺失函数_应生成错误诊断()
    {
        var rule = new WorkflowLegalityRule();
        const string json = """
        {
          "Workflows": {
            "Demo": {
              "steps": [
                { "FunctionName": "Ok" },
                { "FunctionName": "MissingStep" }
              ]
            }
          }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var registry = CreateObjectRegistry(new[] { "Ok" });
        var context = new ValidationContext(ValidationMode.Loose, "wf.json", json);
        context.WithProvider<IObjectRegistry>(registry);

        var result = rule.ValidateAsync(doc.RootElement, context).GetAwaiter().GetResult();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("WorkflowLegality", result[0].RuleName);
        Assert.IsTrue(result[0].Message.Contains("MissingStep"));
        Assert.IsTrue(result[0].SourceLocation?.JsonPath!.Contains("FunctionName"));
    }

    [TestMethod]
    public void WorkflowLegality_全部注册_应无诊断()
    {
        var rule = new WorkflowLegalityRule();
        using var doc = JsonDocument.Parse("""{ "Workflows": { "W": { "steps": [ { "FunctionName": "Ok" } ] } } }""");
        var context = new ValidationContext();
        context.WithProvider<IObjectRegistry>(CreateObjectRegistry(new[] { "Ok" }));

        var result = rule.ValidateAsync(doc.RootElement, context).GetAwaiter().GetResult();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ScopeGuard_无任何提供者_应跳过()
    {
        var rule = new ScopeGuardRule();
        using var doc = JsonDocument.Parse("""{ "bindings": { "b1": { "source": "obj" } } }""");

        var result = rule.ValidateAsync(doc.RootElement, new ValidationContext()).GetAwaiter().GetResult();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ScopeGuard_对象未注册_应生成错误诊断()
    {
        var rule = new ScopeGuardRule();
        const string json = """
        {
          "bindings": {
            "b1": { "source": "UnknownObj" }
          }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var registry = CreateObjectRegistry(new[] { "KnownObj" });
        var context = new ValidationContext(ValidationMode.Loose, "scope.json", json);
        context.WithProvider<IObjectRegistry>(registry);

        var result = rule.ValidateAsync(doc.RootElement, context).GetAwaiter().GetResult();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("ScopeGuard", result[0].RuleName);
        Assert.AreEqual(Severity.Error, result[0].Severity);
        Assert.IsTrue(result[0].Message.Contains("UnknownObj"));
    }

    [TestMethod]
    public void ScopeGuard_引用作用域为default_对象声明于其他作用域_应生成错误诊断()
    {
        var rule = new ScopeGuardRule();
        const string json = """
        {
          "bindings": {
            "b1": { "source": "globalObj" }
          }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var registry = CreateObjectRegistry(new[] { "globalObj" });
        var scopeProvider = new FakeScopeProvider { ["globalObj"] = "global-scope" };
        var context = new ValidationContext(ValidationMode.Loose, "scope.json", json);
        context.WithProvider<IObjectRegistry>(registry);
        context.WithProvider<IScopeProvider>(scopeProvider);

        var result = rule.ValidateAsync(doc.RootElement, context).GetAwaiter().GetResult();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(Severity.Error, result[0].Severity);
        Assert.IsTrue(result[0].Message.Contains("global-scope"));
        Assert.IsTrue(result[0].Message.Contains("default"));
    }

    [TestMethod]
    public void ScopeGuard_作用域一致_应无诊断()
    {
        var rule = new ScopeGuardRule();
        const string json = """
        {
          "bindings": {
            "b1": { "source": "localObj" }
          }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var registry = CreateObjectRegistry(new[] { "localObj" });
        var scopeProvider = new FakeScopeProvider { ["localObj"] = "default" };
        var context = new ValidationContext();
        context.WithProvider<IObjectRegistry>(registry);
        context.WithProvider<IScopeProvider>(scopeProvider);

        var result = rule.ValidateAsync(doc.RootElement, context).GetAwaiter().GetResult();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ScopeGuard_作用域未知_应生成警告诊断()
    {
        var rule = new ScopeGuardRule();
        const string json = """
        {
          "bindings": {
            "b1": { "source": "obj" }
          }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var registry = CreateObjectRegistry(new[] { "obj" });
        var scopeProvider = new FakeScopeProvider();
        var context = new ValidationContext();
        context.WithProvider<IObjectRegistry>(registry);
        context.WithProvider<IScopeProvider>(scopeProvider);

        var result = rule.ValidateAsync(doc.RootElement, context).GetAwaiter().GetResult();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(Severity.Warning, result[0].Severity);
        Assert.IsTrue(result[0].Message.Contains("作用域未知"));
    }


    private static IContractSignatureProvider CreateSignatureProvider(IEnumerable<string> functions)
        => new CompositeValidationProvider("sig", functions.ToDictionary(f => f, _ => (IReadOnlyList<string>)Array.Empty<string>()));

    private static IObjectRegistry CreateObjectRegistry(IEnumerable<string> objects)
        => new CompositeValidationProvider("obj", objects: objects);

    private sealed class FakeScopeProvider : IScopeProvider
    {
        private readonly Dictionary<string, string> _scopes = new();

        public string? this[string name]
        {
            set => _scopes[name] = value!;
        }

        public string? GetObjectScope(string objectName)
            => _scopes.TryGetValue(objectName, out var scope) ? scope : null;
    }
}