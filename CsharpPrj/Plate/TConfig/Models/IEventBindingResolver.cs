
namespace TConfig;

public interface IEventBindingResolver
{
    IEnumerable<ResolvedEventBinding> Resolve(object control, EventConfig eventConfig);

    IEventBindingInfo? FindEventBinding(string sourceName, string? sourceType = null);
}

public class ResolvedEventBinding
{
    public string EventName { get; set; } = string.Empty;

    public string FunctionName { get; set; } = string.Empty;

    public Delegate? Handler { get; set; }
}
