
using System.Text;
using System.Text.Json.Nodes;
using Modules.Parsing.Parsing;
using Common.PlaceholderFramework.TemplateExpansion.Abstractions;

namespace Common.PlaceholderFramework.TemplateExpansion.Diagnostics;

public class DiagnosticEntry
{
    public DiagnosticLevel Level { get; set; }

    public string Category { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? TemplateName { get; set; }

    public string? PropertyPath { get; set; }

    public string? Suggestion { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public enum DiagnosticLevel
{
    Info,
    Warning,
    Error
}

public class TemplateDiagnostics
{
    private readonly ITemplateStorage _storage;
    private readonly List<DiagnosticEntry> _entries = new();
    private readonly HashSet<string> _visitedTemplates = new();

    public TemplateDiagnostics(ITemplateStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public IReadOnlyList<DiagnosticEntry> Entries => _entries.AsReadOnly();

    public void Clear() => _entries.Clear();

    public void Analyze(JsonObject json, string? sourceName = null)
    {
        _visitedTemplates.Clear();
        AnalyzeNode(json, "$", sourceName);
    }

    public List<string> TraceReferenceChain(string templateName)
    {
        var chain = new List<string>();
        var visited = new HashSet<string>();
        TraceChain(templateName, chain, visited);
        return chain;
    }

    public List<DiagnosticEntry> ValidateAllReferences()
    {
        var results = new List<DiagnosticEntry>();

        foreach (var name in _storage.GetCompositeNames())
        {
            var composite = _storage.GetComposite(name);
            if (composite == null) continue;

            ValidateReferences(name, composite, results);
        }

        return results;
    }

    public string GenerateReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Template Diagnostics Report ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        var compositeNames = _storage.GetCompositeNames().ToList();
        var styleNames = _storage.GetStyleNames().ToList();
        sb.AppendLine($"Composites: {compositeNames.Count}");
        sb.AppendLine($"Styles: {styleNames.Count}");
        sb.AppendLine();

        if (_entries.Count > 0)
        {
            sb.AppendLine("=== Diagnostic Entries ===");
            foreach (var entry in _entries.OrderBy(e => e.Timestamp))
            {
                sb.AppendLine($"[{entry.Level}] {entry.Category}: {entry.Message}");
                if (entry.TemplateName != null)
                    sb.AppendLine($"  Template: {entry.TemplateName}");
                if (entry.PropertyPath != null)
                    sb.AppendLine($"  Path: {entry.PropertyPath}");
                if (entry.Suggestion != null)
                    sb.AppendLine($"  Suggestion: {entry.Suggestion}");
                sb.AppendLine();
            }
        }

        var unused = FindUnusedTemplates();
        if (unused.Count > 0)
        {
            sb.AppendLine("=== Unused Templates ===");
            foreach (var name in unused)
                sb.AppendLine($"  - {name}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public List<string> FindUnusedTemplates()
    {
        var referenced = new HashSet<string>();
        var allNames = _storage.GetCompositeNames().ToList();

        foreach (var name in allNames)
        {
            var composite = _storage.GetComposite(name);
            if (composite == null) continue;

            CollectReferences(composite, referenced);
        }

        return allNames.Where(n => !referenced.Contains(n) && !IsBuiltInTemplate(n)).ToList();
    }

    private void AnalyzeNode(JsonNode node, string path, string? sourceName)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("template", out var templateNode) &&
                templateNode is JsonValue templateVal &&
                templateVal.TryGetValue<string>(out var templateName))
            {
                var resolvedName = PrefixResolver.ResolveName(templateName, PrefixResolver.Template);

                if (_storage.HasComposite(resolvedName))
                {
                    if (_visitedTemplates.Add(resolvedName))
                    {
                        var composite = _storage.GetComposite(resolvedName);
                        if (composite != null)
                            AnalyzeNode(composite, $"{path}.template[{resolvedName}]", resolvedName);
                    }
                }
                else
                {
                    _entries.Add(new DiagnosticEntry
                    {
                        Level = DiagnosticLevel.Warning,
                        Category = "MissingTemplate",
                        Message = $"Template '{resolvedName}' not found",
                        TemplateName = resolvedName,
                        PropertyPath = path,
                        Suggestion = $"Ensure '{resolvedName}' is defined in composites.json"
                    });
                }
            }

            if (obj.TryGetPropertyValue("extends", out var extendsNode) &&
                extendsNode is JsonValue extendsVal &&
                extendsVal.TryGetValue<string>(out var extendsName))
            {
                var resolvedExtends = PrefixResolver.ResolveName(extendsName, PrefixResolver.Extends);

                if (!_storage.HasComposite(resolvedExtends))
                {
                    _entries.Add(new DiagnosticEntry
                    {
                        Level = DiagnosticLevel.Warning,
                        Category = "MissingExtends",
                        Message = $"Extends target '{resolvedExtends}' not found",
                        TemplateName = resolvedExtends,
                        PropertyPath = path,
                        Suggestion = $"Ensure '{resolvedExtends}' is defined in composites.json"
                    });
                }
            }

            foreach (var kv in obj)
            {
                if (kv.Value != null)
                    AnalyzeNode(kv.Value, $"{path}.{kv.Key}", sourceName);
            }
        }
        else if (node is JsonArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] != null)
                    AnalyzeNode(arr[i], $"{path}[{i}]", sourceName);
            }
        }
    }

    private bool TraceChain(string templateName, List<string> chain, HashSet<string> visited)
    {
        if (visited.Contains(templateName))
        {
            chain.Add($"*** CIRCULAR: {templateName} ***");
            return false;
        }

        if (!_storage.HasComposite(templateName))
        {
            chain.Add($"*** NOT FOUND: {templateName} ***");
            return false;
        }

        visited.Add(templateName);
        chain.Add(templateName);

        var composite = _storage.GetComposite(templateName);
        if (composite != null)
        {
            if (composite.TryGetPropertyValue("extends", out var extendsNode) &&
                extendsNode is JsonValue extendsVal &&
                extendsVal.TryGetValue<string>(out var extendsName))
            {
                var resolvedExtends = PrefixResolver.ResolveName(extendsName, PrefixResolver.Extends);

                chain.Add($"  └─ extends: {resolvedExtends}");
                TraceChain(resolvedExtends, chain, visited);
            }

            FindTemplateReferences(composite, chain, visited, "  ");
        }

        return true;
    }

    private void FindTemplateReferences(JsonObject obj, List<string> chain, HashSet<string> visited, string indent)
    {
        foreach (var kv in obj)
        {
            if (kv.Value is JsonObject childObj)
            {
                if (childObj.TryGetPropertyValue("template", out var templateNode) &&
                    templateNode is JsonValue templateVal &&
                    templateVal.TryGetValue<string>(out var templateName))
                {
                    var resolvedName = PrefixResolver.ResolveName(templateName, PrefixResolver.Template);

                    chain.Add($"{indent}└─ {kv.Key}: template -> {resolvedName}");
                    TraceChain(resolvedName, chain, visited);
                }
                else
                {
                    FindTemplateReferences(childObj, chain, visited, $"{indent}  ");
                }
            }
            else if (kv.Value is JsonArray arr)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is JsonObject arrObj)
                    {
                        if (arrObj.TryGetPropertyValue("template", out var templateNode) &&
                            templateNode is JsonValue templateVal &&
                            templateVal.TryGetValue<string>(out var templateName))
                        {
                            var resolvedName = PrefixResolver.ResolveName(templateName, PrefixResolver.Template);

                            chain.Add($"{indent}└─ [{i}]: template -> {resolvedName}");
                            TraceChain(resolvedName, chain, visited);
                        }
                        else
                        {
                            FindTemplateReferences(arrObj, chain, visited, $"{indent}  ");
                        }
                    }
                }
            }
        }
    }

    private void ValidateReferences(string name, JsonObject obj, List<DiagnosticEntry> results)
    {
        foreach (var kv in obj)
        {
            if (kv.Value is JsonObject childObj)
            {
                if (childObj.TryGetPropertyValue("template", out var templateNode) &&
                    templateNode is JsonValue templateVal &&
                    templateVal.TryGetValue<string>(out var templateName))
                {
                    var resolvedName = PrefixResolver.ResolveName(templateName, PrefixResolver.Template);

                    if (!_storage.HasComposite(resolvedName))
                    {
                        results.Add(new DiagnosticEntry
                        {
                            Level = DiagnosticLevel.Error,
                            Category = "BrokenReference",
                            Message = $"Template '{name}' references missing template '{resolvedName}'",
                            TemplateName = name,
                            PropertyPath = kv.Key,
                            Suggestion = $"Add '{resolvedName}' to composites.json or fix the reference"
                        });
                    }
                }

                ValidateReferences(name, childObj, results);
            }
        }
    }

    private void CollectReferences(JsonObject obj, HashSet<string> references)
    {
        foreach (var kv in obj)
        {
            if (kv.Value is JsonObject childObj)
            {
                if (childObj.TryGetPropertyValue("template", out var templateNode) &&
                    templateNode is JsonValue templateVal &&
                    templateVal.TryGetValue<string>(out var templateName))
                {
                    var resolvedName = PrefixResolver.ResolveName(templateName, PrefixResolver.Template);

                    references.Add(resolvedName);
                }

                if (childObj.TryGetPropertyValue("extends", out var extendsNode) &&
                    extendsNode is JsonValue extendsVal &&
                    extendsVal.TryGetValue<string>(out var extendsName))
                {
                    var resolvedExtends = PrefixResolver.ResolveName(extendsName, PrefixResolver.Extends);

                    references.Add(resolvedExtends);
                }

                CollectReferences(childObj, references);
            }
        }
    }

    private static bool IsBuiltInTemplate(string name) =>
        name.StartsWith("_");
}
