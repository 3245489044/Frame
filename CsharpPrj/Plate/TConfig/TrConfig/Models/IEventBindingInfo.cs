
namespace TConfig;

public interface IEventBindingInfo
{
    string FunctionName { get; }

    string EventType { get; }

    string? Output { get; }

    IReadOnlyList<string> FunctionNames { get; }
}
