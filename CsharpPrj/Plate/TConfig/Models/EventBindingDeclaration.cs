
using TConfig.Declarative;

namespace TConfig;

public interface IDeclaration
{
    string Name { get; }
}

public class EventBindingDeclaration<TParameter> : IDeclaration, TConfig.Declarative.IDeclaration
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string DeclarationType { get; set; } = "event-binding";

    public string SourceName { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string FunctionName { get; set; } = string.Empty;

    public TParameter? Parameters { get; set; }

    public string? OutputTarget { get; set; }
}
