
using System.Threading.Tasks;

namespace TConfig
{
    public interface IEventBinder
    {
        bool TryBindEvent(
            object eventBus,
            object control,
            string normalizedEventName,
            Delegate handler,
            object errors);

        void RecordBoundControl(
            object input,
            object control,
            int boundEventCount,
            string diagnosticKey,
            object? eventsConfig = null,
            string? eventName = null,
            string? functionName = null);

        Task HandleExecutionResult(ExecutionResult result);

        void WriteOutputToControl(object sender, string outputTarget, object value);
    }
}
