
namespace UniversalMiddleware.EventPipeline
{
    public interface IEventDispatcher
    {
        Task DispatchAsync(Func<Task> action);

        void Dispatch(Action action);
    }
}


