
using Modules.EventFramework.Abstractions;

namespace Modules.EventFramework.Services;

public interface IEventTriggerService
{
    Task<IEventTriggerResult> TriggerEventAsync(
        IEventSourceNode source, string eventName, object? eventArgs);
}

public interface IEventTriggerResult : TConfig.IEventTriggerResult
{
}
