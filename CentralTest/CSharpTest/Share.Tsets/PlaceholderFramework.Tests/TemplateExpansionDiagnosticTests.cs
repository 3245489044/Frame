
using System.Text.Json;
using System.Text.Json.Nodes;
using BasicMethod.Pipeline;
using Common.PlaceholderFramework.TemplateExpansion.Abstractions;
using Common.PlaceholderFramework.TemplateExpansion.Converters;
using Common.PlaceholderFramework.TemplateExpansion.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PlaceholderFramework.Tests;

[TestClass]
public class TemplateExpansionDiagnosticTests
{
    [TestMethod]
    public void Diagnose_DeepClone_Basic()
    {
        var original = new JsonObject { ["key"] = JsonValue.Create("value") };
        var cloned = original.DeepClone().AsObject();
        
        Console.WriteLine($"original parent: {original.Parent?.GetType().Name ?? "null"}");
        Console.WriteLine($"cloned parent: {cloned.Parent?.GetType().Name ?? "null"}");
        
        foreach (var kv in cloned)
        {
            Console.WriteLine($"kv.Key={kv.Key}, kv.Value={kv.Value}, kv.Value.Parent={(kv.Value.Parent?.GetType().Name ?? "null")}");
        }
        
        var target = new JsonObject();
        foreach (var kv in cloned)
        {
            Console.WriteLine($"Adding {kv.Key} to target...");
            target[kv.Key] = kv.Value;
        }
        Console.WriteLine($"Target: {target.ToJsonString()}");
    }

    [TestMethod]
    public void Diagnose_DeepClone_Nested()
    {
        var original = JsonNode.Parse("""{"type":"form","config":{"method":"post","action":"/submit"}}""")!.AsObject();
        var cloned = original.DeepClone().AsObject();
        
        Console.WriteLine($"original parent: {original.Parent?.GetType().Name ?? "null"}");
        Console.WriteLine($"cloned parent: {cloned.Parent?.GetType().Name ?? "null"}");
        
        var config = cloned["config"] as JsonObject;
        Console.WriteLine($"config parent: {config?.Parent?.GetType().Name ?? "null"}");
        
        foreach (var kv in cloned)
        {
            Console.WriteLine($"kv.Key={kv.Key}, kv.Value.Type={kv.Value?.GetType().Name}, kv.Value.Parent={(kv.Value?.Parent?.GetType().Name ?? "null")}");
        }
        
        var target = new JsonObject();
        foreach (var kv in cloned)
        {
            Console.WriteLine($"Adding {kv.Key} to target...");
            target[kv.Key] = kv.Value;
        }
        Console.WriteLine($"Target: {target.ToJsonString()}");
    }

    [TestMethod]
    public void Diagnose_DeepClone_UsingIndexer()
    {
        var original = JsonNode.Parse("""{"type":"form","config":{"method":"post","action":"/submit"}}""")!.AsObject();
        var cloned = original.DeepClone().AsObject();
        
        var target = new JsonObject();
        foreach (var key in cloned.Select(kv => kv.Key).ToList())
        {
            Console.WriteLine($"Adding {key} to target...");
            target[key] = cloned[key]!.DeepClone();
        }
        Console.WriteLine($"Target: {target.ToJsonString()}");
    }

    [TestMethod]
    public void Diagnose_ExtendsConverter()
    {
        var storage = new MockTemplateStorage();
        var converter = new ExtendsConverter(storage);

        var input = JsonNode.Parse("""{"extends":"baseForm","extra":"value"}""")!.AsObject();
        var errors = new ErrorCollector();
        var context = new PipelineContext();

        var result = converter.Process(input, errors, context);

        Console.WriteLine($"Process returned null: {result == null}");
        Console.WriteLine($"Errors: {errors.GetErrorReport()}");

        Assert.IsNotNull(result, "Process returned null");
        Assert.IsInstanceOfType(result, typeof(JsonObject));

        var output = result as JsonObject;
        Assert.IsFalse(output!.ContainsKey("extends"), "extends should be removed");
        Assert.IsTrue(output.ContainsKey("config"), "config should be merged from baseForm");
        Assert.IsTrue(output.ContainsKey("extra"), "extra should be preserved");

        var serialized = JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"ExtendsConverter output: {serialized}");
    }

    [TestMethod]
    public void Diagnose_StyleApplyConverter()
    {
        var storage = new MockTemplateStorage();
        var converter = new StyleApplyConverter(storage);

        var input = JsonNode.Parse("""{"type":"button","style":"primaryBtn","text":"Click"}""")!.AsObject();
        var errors = new ErrorCollector();
        var context = new PipelineContext();

        var result = converter.Process(input, errors, context);

        Console.WriteLine($"Process returned null: {result == null}");
        Console.WriteLine($"Errors: {errors.GetErrorReport()}");

        Assert.IsNotNull(result, "Process returned null");
        Assert.IsInstanceOfType(result, typeof(JsonObject));

        var output = result as JsonObject;
        Assert.IsFalse(output!.ContainsKey("style"), "style should be removed");
        Assert.IsTrue(output.ContainsKey("color"), "color should be merged from style");

        var serialized = JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"StyleApplyConverter output: {serialized}");
    }

    [TestMethod]
    public void Diagnose_FullPipeline()
    {
        var storage = new MockTemplateStorage();
        var pipeline = new JsonPipelineBuilder("diagnose")
            .UseExtends(storage)
            .UseStyleApply(storage)
            .UseTemplateExpand(storage)
            .UseDefaultsApply(storage)
            .UseConfigMerge()
            .UseValidation(false)
            .Build();

        var input = """
        {
            "extends": "baseForm",
            "views": [
                {
                    "type": "form",
                    "controls": [
                        {
                            "type": "button",
                            "style": "primaryBtn",
                            "template": "labeledInput",
                            "label": "Name",
                            "placeholder": "Enter name",
                            "text": "Submit"
                        }
                    ]
                }
            ]
        }
        """;

        var result = pipeline.Execute(input);

        Console.WriteLine($"IsSuccess: {result.IsSuccess}");
        Console.WriteLine($"Status: {result.Status}");
        Console.WriteLine($"Data type: {result.Data?.GetType().FullName ?? "null"}");
        Console.WriteLine($"HasErrors: {result.HasErrors}");
        Console.WriteLine($"HasFatalErrors: {result.HasFatalErrors}");
        Console.WriteLine($"Error report: {result.Errors.GetErrorReport()}");

        if (result.Data is string output)
        {
            Console.WriteLine($"Output: {output}");
        }
    }

    [TestMethod]
    public void Diagnose_Validation()
    {
        var pipeline = new JsonPipelineBuilder("validate")
            .UseValidation(true)
            .Build();

        var result = pipeline.Execute("""{"irrelevant":true}""");

        Console.WriteLine($"IsSuccess: {result.IsSuccess}");
        Console.WriteLine($"Status: {result.Status}");
        Console.WriteLine($"HasErrors: {result.HasErrors}");
        Console.WriteLine($"HasFatalErrors: {result.HasFatalErrors}");
        Console.WriteLine($"Error report: {result.Errors.GetErrorReport()}");
    }

    [TestMethod]
    public void Diagnose_EmptyPipeline()
    {
        var pipeline = new JsonPipelineBuilder("empty").Build();
        var result = pipeline.Execute("""{"key":"value"}""");

        Console.WriteLine($"IsSuccess: {result.IsSuccess}");
        Console.WriteLine($"Status: {result.Status}");
        Console.WriteLine($"Data type: {result.Data?.GetType().FullName ?? "null"}");

        if (result.Data is string output)
        {
            Console.WriteLine($"Output: {output}");
        }
    }

    private class MockTemplateStorage : ITemplateStorage
    {
        private readonly Dictionary<string, JsonObject> _styles = new();
        private readonly Dictionary<string, JsonObject> _composites = new();

        public bool IsInitialized => true;
        public string? StylesPath => null;
        public string? CompositesPath => null;

        public MockTemplateStorage()
        {
            _styles["primaryBtn"] = JsonNode.Parse("""
            {
                "description": "Primary button style",
                "properties": {
                    "color": "#fff",
                    "bgColor": "#007bff",
                    "padding": "8px 16px"
                }
            }
            """)!.AsObject();

            _composites["labeledInput"] = JsonNode.Parse("""
            {
                "type": "labeled-input",
                "controls": [
                    { "type": "label", "text": "${label}" },
                    { "type": "input", "placeholder": "${placeholder}" }
                ],
                "parameters": {
                    "label": { "required": true },
                    "placeholder": { "default": "Enter value" }
                }
            }
            """)!.AsObject();

            _composites["baseForm"] = JsonNode.Parse("""
            {
                "type": "form",
                "config": {
                    "method": "post",
                    "action": "/submit"
                }
            }
            """)!.AsObject();

            _composites["_defaults"] = JsonNode.Parse("""
            {
                "width": "100%",
                "height": "auto",
                "visible": true
            }
            """)!.AsObject();

            _composites["_button_defaults"] = JsonNode.Parse("""
            {
                "cursor": "pointer",
                "border": "1px solid #ccc"
            }
            """)!.AsObject();
        }

        public void Initialize(string stylesPath, string compositesPath) { }
        public void Initialize(IEnumerable<(string stylesPath, string compositesPath)> paths) { }

        public JsonObject? GetStyle(string name) =>
            _styles.TryGetValue(name, out var style) ? style : null;

        public JsonObject? GetComposite(string name) =>
            _composites.TryGetValue(name, out var composite) ? composite : null;

        public bool HasStyle(string name) => _styles.ContainsKey(name);
        public bool HasComposite(string name) => _composites.ContainsKey(name);

        public IEnumerable<string> GetStyleNames() => _styles.Keys;
        public IEnumerable<string> GetCompositeNames() => _composites.Keys;

        public TemplateMetadata? GetMetadata(string name) => null;
        public IEnumerable<string> SearchTemplates(string keyword) => Enumerable.Empty<string>();
        public void RefreshIfNeeded() { }

        public void RegisterComposite(string name, JsonObject composite) => _composites[name] = composite;
        public void RegisterStyle(string name, JsonObject style) => _styles[name] = style;
        public void UnregisterComposite(string name) => _composites.Remove(name);
        public void UnregisterStyle(string name) => _styles.Remove(name);

        public void RegisterComposites(Dictionary<string, JsonObject> definitions)
        {
            foreach (var (key, value) in definitions) _composites[key] = value;
        }
        public void RegisterStyles(Dictionary<string, JsonObject> definitions)
        {
            foreach (var (key, value) in definitions) _styles[key] = value;
        }
        public void UnregisterComposites(IEnumerable<string> names)
        {
            foreach (var name in names) _composites.Remove(name);
        }
        public void UnregisterStyles(IEnumerable<string> names)
        {
            foreach (var name in names) _styles.Remove(name);
        }
        public void ClearComposites() => _composites.Clear();
        public void ClearStyles() => _styles.Clear();
    }
}
