
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;
using ParameterResolver.Validation.Providers;
using ParameterResolver.Validation.Rules;
using ParameterResolver.Validation.Rules.Reference;
using ParameterResolver.Validation.Rules.Runtime;
using ParameterResolver.Validation.Rules.Semantic;
using ParameterResolver.Validation.Rules.Structure;

namespace ParameterResolver.Validation;

public static class ConfigValidatorFactory
{
    public static ValidationEngine CreateDefaultEngine()
        => CreateEngine("DefaultValidationEngine", includeAll: true);

    public static ValidationEngine CreateMinimalEngine()
        => CreateEngine("MinimalValidationEngine", includeStructure: true);

    public static ValidationEngine CreateReferenceEngine()
        => CreateEngine("ReferenceValidationEngine", includeReference: true);

    public static ValidationEngine CreateSemanticEngine()
        => CreateEngine("SemanticValidationEngine", includeSemantic: true);

    public static ValidationEngine CreateEngine(
        string name,
        bool includeAll = false,
        bool includeStructure = false,
        bool includeReference = false,
        bool includeSemantic = false,
        bool includeRuntime = false)
    {
        var engine = new ValidationEngine(name);

        if (includeAll || includeStructure)
        {
            engine.RegisterRule(new EventValueTypeRule());
            engine.RegisterRule(new LayoutFormatRule());
        }

        if (includeAll || includeReference)
        {
            engine.RegisterRule(new TemplateRefRule());
            engine.RegisterRule(new ControlRefRule());
            engine.RegisterRule(new FunctionRefRule());
            engine.RegisterRule(new OutputTargetRule());
        }

        if (includeAll || includeSemantic)
        {
            engine.RegisterRule(new EventBindingFormatRule());
            engine.RegisterRule(new ParameterMappingFormatRule());
        }

        if (includeAll || includeRuntime)
        {
            engine.RegisterRule(new ControlExistenceRule());
        }

        return engine;
    }

    public static async Task<ValidationReport> ValidateJsonAsync(
        string json,
        string? filePath = null,
        ValidationMode mode = ValidationMode.Loose,
        params object[] providers)
    {
        using var doc = JsonDocument.Parse(json);
        return await ValidateJsonAsync(doc, filePath, mode, providers);
    }

    public static async Task<ValidationReport> ValidateJsonAsync(
        JsonDocument document,
        string? filePath = null,
        ValidationMode mode = ValidationMode.Loose,
        params object[] providers)
    {
        var engine = CreateDefaultEngine();
        var context = CreateValidationContext(document, filePath, mode, providers);
        return await engine.ValidateAsync(document.RootElement, context);
    }

    private static ConfigValidationContext CreateValidationContext(
        JsonDocument document,
        string? filePath,
        ValidationMode mode,
        params object[] providers)
    {
        var context = new ConfigValidationContext(mode)
        {
            RawJson = document.RootElement.GetRawText(),
            FilePath = filePath
        };

        foreach (var provider in providers)
        {
            switch (provider)
            {
                case ITemplateRegistryProvider templateRegistry:
                    context.TemplateRegistry = templateRegistry;
                    break;
                case IFunctionRegistryProvider functionRegistry:
                    context.FunctionRegistry = functionRegistry;
                    break;
                case IControlTreeProvider controlTree:
                    context.ControlTree = controlTree;
                    break;
            }
        }

        return context;
    }
}
