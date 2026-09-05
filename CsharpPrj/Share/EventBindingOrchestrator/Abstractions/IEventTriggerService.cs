
using System.Threading.Tasks;

namespace Share.EventBindingOrchestrator.Abstractions
{
    public interface IEventTriggerService
    {
        Task<IEventTriggerResult> TriggerEventAsync(string controlName, string eventType, string value);
    }
}
