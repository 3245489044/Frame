
namespace TConfig;

public interface IEventSubscription : IDisposable
{
    bool IsDisposed { get; }
}
