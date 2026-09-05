
namespace TConfig;

public class EventContext
{
    public string? FunctionName { get; init; }

    public string EventName { get; init; } = "";

    public object? Source { get; init; }

    public object? EventArgs { get; init; }

    public object? Sender { get; init; }

    public string? OutputTarget { get; init; }

    public string? OutputKey { get; init; }

    public ITrConfigRegistry? Registry { get; init; }

    public Action<string, string, object?>? OnResult { get; init; }

    public Dictionary<string, object> Data { get; } = new();
}
