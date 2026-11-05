
using TConfig.ContextFrame.Abstractions;

namespace Modules.InternalStorage.Initialization;

public class InitializationContext : IInitializationContext
{
    public IStorageCell Storage { get; }

    public IStorageRegistry Registry { get; }

    public CancellationToken CancellationToken { get; }

    public InitializationContext(
        IStorageCell storage,
        IStorageRegistry registry,
        CancellationToken cancellationToken = default)
    {
        Storage = storage ?? throw new ArgumentNullException(nameof(storage));
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        CancellationToken = cancellationToken;
    }
}
