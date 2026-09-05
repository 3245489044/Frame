
namespace UniversalMiddleware.EventPipeline
{
    public interface IEventSuppressor
    {
        IDisposable Suppress(object control);

        IDisposable SuppressAll(IEnumerable<object> controls);

        bool IsSuppressed(object control);
    }

}


