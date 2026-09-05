namespace CSG.Unified;

public class CsgFacadeOptions
{
    public bool EnableCache { get; set; } = true;

    public bool UseDefaultRules { get; set; } = true;

    public bool AutoRegisterBuiltInAdapters { get; set; }

    public List<IOptimizationRule>? CustomRules { get; set; }

    public IAdapterRegistry? AdapterRegistry { get; set; }

    public ITransformEngine? TransformEngine { get; set; }

    public static CsgFacadeOptions Default => new();
}
