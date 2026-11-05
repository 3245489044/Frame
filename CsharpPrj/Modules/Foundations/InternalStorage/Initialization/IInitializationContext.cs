
using TConfig.ContextFrame.Abstractions;

namespace Modules.InternalStorage.Initialization;

public interface IInitializationContext
{
    IStorageCell Storage { get; }

    IStorageRegistry Registry { get; }

    CancellationToken CancellationToken { get; }
}
