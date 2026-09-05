namespace CSG.Unified;

public class AdapterCapabilities
{
    public string AdapterName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Version { get; init; } = "1.0";

    public List<string> SupportedFormTypes { get; init; } = new();

    public List<string> SupportedOperations { get; init; } = new();

    public List<TransformDirection> SupportedDirections { get; init; } = new();

    public string Source { get; init; } = string.Empty;

    public string[] Tags { get; init; } = Array.Empty<string>();

    public Dictionary<string, DomainOptimizationHints> OperationHints { get; init; } = new();

    public DomainOptimizationHints GetOptimizationHints(string operationName)
    {
        return OperationHints.TryGetValue(operationName, out var hints)
            ? hints
            : DomainOptimizationHints.Default;
    }

    public AdapterCapabilities WithOperationHint(string operationName, DomainOptimizationHints hints)
    {
        OperationHints[operationName] = hints;
        return this;
    }

    public override string ToString()
        => $"{AdapterName} v{Version} [{Source}]: {string.Join(", ", SupportedOperations)}";

}
