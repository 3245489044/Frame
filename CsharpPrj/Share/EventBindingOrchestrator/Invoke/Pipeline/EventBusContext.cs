
using System.Reflection;

namespace Share.EventBindingOrchestrator.Adapters;

public record EventBusContext
{

    public object Control { get; init; } = null!;

    public string RawEventName { get; init; } = string.Empty;

    public Delegate Handler { get; init; } = null!;


    public string NormalizedEventName { get; set; } = string.Empty;

    public EventInfo? EventInfo { get; set; }

    public Type? EventHandlerType { get; set; }

    public bool IsDirectEventHandler { get; set; }

    public string ControlName { get; set; } = string.Empty;


    public bool BindingSuccess { get; set; }

    public string? BindingError { get; set; }
}
