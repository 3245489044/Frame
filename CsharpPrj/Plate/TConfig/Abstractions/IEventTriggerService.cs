
using System.Threading.Tasks;

namespace TConfig
{
    public interface IEventTriggerService
    {
        Task<IEventTriggerResult> TriggerEventAsync(string eventName, object? sender, object? args);

        Task<IEventTriggerResult> TriggerAndExecuteAsync(string eventName, object? sender, object? args);
    }
}
