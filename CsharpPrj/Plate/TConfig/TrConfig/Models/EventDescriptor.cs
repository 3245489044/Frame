
using TConfig.Declarative;

namespace TConfig;

public class EventDescriptor : IDeclaration
{
    public string Name => $"{ControlName}.{EventName}";

    public string Description => $"绑定 {ControlName}.{EventName} → {FunctionName}";

    public string DeclarationType => "event-binding";

    public string ControlName { get; init; } = "";

    public string EventName { get; init; } = "";

    public string FunctionName { get; init; } = "";

    public IReadOnlyList<ParameterMapping> Parameters { get; init; } = Array.Empty<ParameterMapping>();

    public string? OutputTarget { get; init; }

    public string? OutputKey { get; init; }

    public IReadOnlyDictionary<string, string>? OutputMapping { get; init; }
}
