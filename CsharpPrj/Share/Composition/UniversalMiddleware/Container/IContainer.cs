
#nullable enable
using UniversalMiddleware.Abstractions;

namespace UniversalMiddleware.Container;

public interface IContainer : IConfigContainer
{
    ContainerNode Node { get; }

    Lifecycle.ContainerLifecycle Lifecycle { get; }

    ContainerEventManager EventManager { get; }
}
