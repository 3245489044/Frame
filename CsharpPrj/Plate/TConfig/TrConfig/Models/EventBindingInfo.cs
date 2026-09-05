
namespace TConfig;

public class EventBindingInfo : TConfig.IEventBindingInfo
{
    public string FunctionName => FunctionNames is { Count: > 0 } ? FunctionNames[0] : "";

    public string EventType { get; set; } = "";

    public string? Output { get; set; }

    public string? OutputKey { get; set; }

    public IReadOnlyList<string> FunctionNames { get; set; } = Array.Empty<string>();
}
