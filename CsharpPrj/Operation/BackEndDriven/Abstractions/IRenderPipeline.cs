
#nullable enable
using System.Threading.Tasks;

namespace BackEndDriven.Abstractions
{
    public interface IRenderPipeline
    {
        string Name { get; }

        Task ExecuteCreateStageAsync(object target, object config);

        Task ExecuteLayoutStageAsync(object target);

        Task ExecuteBindEventsStageAsync(object target, object config);

        object? GetActiveSession(object target);

        void DisposeSession(object target);
    }
}
