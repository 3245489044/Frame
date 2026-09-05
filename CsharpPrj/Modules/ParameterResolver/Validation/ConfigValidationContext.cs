
using ParameterResolver.Validation.Providers;

namespace ParameterResolver.Validation;

public enum ValidationMode
{
    Strict,

    Loose,

    Silent
}

public class ConfigValidationContext
{
    public ValidationMode Mode { get; set; } = ValidationMode.Loose;

    public ITemplateRegistryProvider? TemplateRegistry { get; set; }

    public IFunctionRegistryProvider? FunctionRegistry { get; set; }

    public IControlTreeProvider? ControlTree { get; set; }

    public string? RawJson { get; set; }

    public string? FilePath { get; set; }

    public Dictionary<string, object?> CustomData { get; } = new();

    public ConfigValidationContext(ValidationMode mode = ValidationMode.Loose)
    {
        Mode = mode;
    }

    public bool IsStrict => Mode == ValidationMode.Strict;

    public bool IsLoose => Mode == ValidationMode.Loose;

    public bool IsSilent => Mode == ValidationMode.Silent;

    public ConfigValidationContext CreateScope()
    {
        return new ConfigValidationContext(Mode)
        {
            TemplateRegistry = TemplateRegistry,
            FunctionRegistry = FunctionRegistry,
            ControlTree = ControlTree,
            RawJson = RawJson,
            FilePath = FilePath
        };
    }
}
